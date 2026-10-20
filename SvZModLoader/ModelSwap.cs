using System;
using System.Collections.Generic;
using UnityEngine;

namespace SvZModLoader
{
	// Shows a different model in place of a character while keeping all of the character's behaviour:
	// the original skeleton keeps playing the game's animations (hidden), and every frame the new model
	// moves with it in one (or both) of two ways:
	//  - [bones] retargeting: each mapped model bone gets the same rotation, relative to its rest pose,
	//    as its original bone has relative to the original's rest pose. Limbs whose rest poses point
	//    different ways (a T-pose skeleton driving an A-pose model) are first lined up using the
	//    direction to the next mapped bone down the chain, unless "alignRest = no". The topmost mapped
	//    bone (the hips) also follows the original's movement.
	//  - follow: the whole model moves with one original bone and plays its own animations, which the
	//    game starts by name like the original's (see ModAssets.AddModel).
	// Bones listed under [attach] work the other way: the original bone is moved onto the new model's
	// bone, so weapons and effects the game attaches there sit in the new model's hands.
	internal class ModelSwap : MonoBehaviour
	{
		internal const string ModelPrefix = "SvZModModel:";

		internal class Config
		{
			public readonly List<string[]> Bones = new List<string[]>();
			public readonly List<string[]> Attach = new List<string[]>();
			public bool AlignRest = true;
			public string Follow;
			public LaserBeam.Settings Laser;
		}

		private class Pair
		{
			public Transform Original;
			public Transform Model;
			public Quaternion OriginalRest;
			public Quaternion ModelRest;
			public Vector3 OriginalRestPosition;
			public Vector3 ModelRestPosition;
			public Quaternion Correction = Quaternion.identity;
			public int Depth;
		}

		// Model root name -> mapping, registered when the variant is built.
		internal static readonly Dictionary<string, Config> Configs = new Dictionary<string, Config>();

		private readonly List<Pair> m_Pairs = new List<Pair>();
		private readonly List<Transform[]> m_Attach = new List<Transform[]>();
		private Pair m_Root;
		private float m_MoveScale = 1f;
		private Transform m_Model;
		private Transform m_Follow;
		private Vector3 m_FollowRest;
		private Vector3 m_ModelRest;

		// Set when the model fires lasers instead of the character's bullets (see ProjectilePatches).
		internal LaserBeam.Settings Laser { get; private set; }
		internal Transform LaserOrigin { get; private set; }

		private void Awake()
		{
			Transform model = null;
			foreach (Transform child in transform)
			{
				if (child.name.StartsWith(ModelPrefix, StringComparison.Ordinal))
				{
					model = child;
				}
			}
			if (model == null || !Configs.TryGetValue(model.name, out Config config))
			{
				enabled = false;
				return;
			}
			m_Model = model;
			Dictionary<string, Transform> originalBones = BonesByName(transform, model);
			Dictionary<string, Transform> modelBones = BonesByName(model, null);
			if (config.Bones.Count > 0)
			{
				SetUpRetargeting(config, originalBones, modelBones);
			}
			if (!string.IsNullOrEmpty(config.Follow))
			{
				m_Follow = Find(originalBones, config.Follow);
				if (m_Follow == null)
				{
					Plugin.Log.LogWarning($"{model.name}: follow bone '{config.Follow}' not found.");
				}
				else
				{
					m_FollowRest = transform.InverseTransformPoint(m_Follow.position);
					m_ModelRest = transform.InverseTransformPoint(model.position);
				}
			}
			foreach (string[] map in config.Attach)
			{
				Transform original = Find(originalBones, map[0]);
				Transform target = Find(modelBones, map[1]);
				if (original != null && target != null)
				{
					m_Attach.Add(new[] { original, target });
				}
			}
			if (config.Laser != null)
			{
				Laser = config.Laser;
				LaserOrigin = Find(modelBones, config.Laser.From) ?? model;
			}
		}

		private void SetUpRetargeting(Config config, Dictionary<string, Transform> originalBones, Dictionary<string, Transform> modelBones)
		{
			Transform model = m_Model;
			foreach (string[] map in config.Bones)
			{
				Transform original = Find(originalBones, map[0]);
				Transform target = Find(modelBones, map[1]);
				if (original == null || target == null)
				{
					Plugin.Log.LogWarning($"{model.name}: bone '{(original == null ? map[0] : map[1])}' not found; skipping it.");
					continue;
				}
				// Rest poses: both skeletons as saved (Awake runs before any animation is applied).
				var pair = new Pair { Original = original, Model = target, Depth = Depth(target, model) };
				pair.OriginalRest = Quaternion.Inverse(transform.rotation) * original.rotation;
				pair.OriginalRestPosition = transform.InverseTransformPoint(original.position);
				pair.ModelRest = Quaternion.Inverse(transform.rotation) * target.rotation;
				pair.ModelRestPosition = transform.InverseTransformPoint(target.position);
				m_Pairs.Add(pair);
			}
			if (m_Pairs.Count == 0)
			{
				return;
			}
			// Parents before children, so setting a parent never undoes a child.
			m_Pairs.Sort((a, b) => a.Depth.CompareTo(b.Depth));
			m_Root = m_Pairs[0];
			// Stand the model where the original stands: its hips over the original's hips.
			Vector3 shift = m_Root.OriginalRestPosition - m_Root.ModelRestPosition;
			shift.y = 0f;
			model.position += transform.TransformVector(shift);
			foreach (Pair pair in m_Pairs)
			{
				pair.ModelRestPosition += shift;
			}
			if (config.AlignRest)
			{
				AlignRestPoses();
			}
			// The hips move by the original's movement, scaled by how much higher the model's hips are.
			float originalHeight = m_Root.OriginalRestPosition.y - LowestRest(true);
			float modelHeight = m_Root.ModelRestPosition.y - LowestRest(false);
			if (originalHeight > 1e-4f && modelHeight > 1e-4f)
			{
				m_MoveScale = modelHeight / originalHeight;
			}
		}

		private void AlignRestPoses()
		{
			foreach (Pair pair in m_Pairs)
			{
				// Line up with the nearest mapped bone below this one; where there are several (the upper
				// spine has the neck and both shoulders), the one whose rest directions already agree best.
				Pair next = null;
				float nextAgreement = 0f;
				foreach (Pair other in m_Pairs)
				{
					if (other == pair || !other.Model.IsChildOf(pair.Model) || !other.Original.IsChildOf(pair.Original))
					{
						continue;
					}
					Vector3 from = other.ModelRestPosition - pair.ModelRestPosition;
					Vector3 to = other.OriginalRestPosition - pair.OriginalRestPosition;
					if (from.sqrMagnitude < 1e-8f || to.sqrMagnitude < 1e-8f)
					{
						continue;
					}
					float agreement = Vector3.Dot(from.normalized, to.normalized);
					if (next == null || other.Depth < next.Depth || (other.Depth == next.Depth && agreement > nextAgreement))
					{
						next = other;
						nextAgreement = agreement;
					}
				}
				if (next != null)
				{
					pair.Correction = Quaternion.FromToRotation(next.ModelRestPosition - pair.ModelRestPosition, next.OriginalRestPosition - pair.OriginalRestPosition);
				}
			}
		}

		private float LowestRest(bool original)
		{
			float lowest = float.MaxValue;
			foreach (Pair pair in m_Pairs)
			{
				lowest = Mathf.Min(lowest, original ? pair.OriginalRestPosition.y : pair.ModelRestPosition.y);
			}
			return lowest;
		}

		private void LateUpdate()
		{
			Quaternion root = transform.rotation;
			foreach (Pair pair in m_Pairs)
			{
				Quaternion change = pair.Original.rotation * Quaternion.Inverse(root * pair.OriginalRest);
				pair.Model.rotation = change * root * pair.Correction * pair.ModelRest;
			}
			if (m_Root != null)
			{
				Vector3 moved = m_Root.Original.position - transform.TransformPoint(m_Root.OriginalRestPosition);
				m_Root.Model.position = transform.TransformPoint(m_Root.ModelRestPosition) + moved * m_MoveScale;
			}
			if (m_Follow != null)
			{
				m_Model.position = transform.TransformPoint(m_ModelRest) + (m_Follow.position - transform.TransformPoint(m_FollowRest));
			}
			foreach (Transform[] attach in m_Attach)
			{
				attach[0].position = attach[1].position;
			}
		}

		internal static Dictionary<string, Transform> BonesByName(Transform root, Transform skip)
		{
			var result = new Dictionary<string, Transform>();
			foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
			{
				if (skip != null && t.IsChildOf(skip))
				{
					continue;
				}
				string name = t.name.ToLowerInvariant();
				if (!result.ContainsKey(name))
				{
					result[name] = t;
				}
				int colon = name.LastIndexOf(':');
				if (colon >= 0 && !result.ContainsKey(name.Substring(colon + 1)))
				{
					result[name.Substring(colon + 1)] = t;
				}
			}
			return result;
		}

		// Bone names match without regard to case, and with or without a "Namespace:" prefix.
		internal static Transform Find(Dictionary<string, Transform> bones, string name)
		{
			bones.TryGetValue(name.Trim().ToLowerInvariant(), out Transform bone);
			return bone;
		}

		private static int Depth(Transform t, Transform root)
		{
			int depth = 0;
			for (; t != null && t != root; t = t.parent)
			{
				depth++;
			}
			return depth;
		}
	}
}
