using System;
using System.Collections.Generic;
using System.IO;
using SvZModding;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SvZModLoader
{
	// Serves game asset requests (Resources.Load paths) from mods before falling back to the game:
	//  1. asset bundles   Mods/<mod>/Bundles/*  - any asset under a folder named "Resources" inside
	//                     the bundle answers that path, e.g. Assets/MyMod/Resources/Characters/PFBoss.prefab
	//                     -> "Characters/PFBoss" (build with Unity 2017.4 to match the game)
	//  2. prefab variants Mods/<mod>/Resources/<path>.variant.txt - an existing prefab with a new
	//                     scale / texture / tint, e.g. a new enemy model without any tools
	//  3. loose files     Mods/<mod>/Resources/<path>.png|.jpg|.wav
	// Textures can also be replaced by name wherever materials use them: Mods/<mod>/Textures/<name>.png.
	internal static class ModAssets
	{
		private struct BundleAsset
		{
			public AssetBundle bundle;
			public string name;
		}

		private static readonly Dictionary<string, BundleAsset> s_BundleAssets = new Dictionary<string, BundleAsset>();
		private static readonly Dictionary<string, Object> s_Cache = new Dictionary<string, Object>();
		private static readonly Dictionary<string, Texture2D> s_TextureFiles = new Dictionary<string, Texture2D>();
		private static readonly HashSet<int> s_Retextured = new HashSet<int>();
		private static readonly Dictionary<string, GameObject> s_Models = new Dictionary<string, GameObject>();
		private static readonly string[] s_TextureProperties =
		{
			"_MainTex", "_SecondTex", "_BlendTex", "_BumpMap", "_C1Tex", "_C2Tex", "_OffsetTex", "_ColorControl", "_Background"
		};
		private static readonly string[] s_ColorProperties = { "_Color", "_MainColor", "_TintColor" };

		private static GameObject s_TemplateRoot;
		private static TextAsset s_EmptyText;

		public static void Init()
		{
			foreach (ModInfo mod in ModCatalog.Mods)
			{
				if (!mod.Allows(ModCategory.Models) || !Directory.Exists(mod.BundlesDir))
				{
					continue;
				}
				foreach (string file in Directory.GetFiles(mod.BundlesDir))
				{
					if (file.EndsWith(".manifest", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					AssetBundle bundle = AssetBundle.LoadFromFile(file);
					if (bundle == null)
					{
						Plugin.Log.LogError($"Couldn't load asset bundle {file} (bundles must be built with Unity 2017.4)");
						continue;
					}
					int count = 0;
					foreach (string asset in bundle.GetAllAssetNames())
					{
						int at = asset.LastIndexOf("/resources/", StringComparison.Ordinal);
						if (at >= 0)
						{
							s_BundleAssets[ModCatalog.KeyFor(asset.Substring(at + "/resources/".Length))] = new BundleAsset { bundle = bundle, name = asset };
							count++;
						}
					}
					Plugin.Log.LogInfo($"Asset bundle {mod.Name}/{Path.GetFileName(file)}: {count} assets");
				}
			}
		}

		// Stand-ins for Resources.Load; ResourcesRedirect points the game's calls here.
		public static Object Load(string path)
		{
			return Load(path, typeof(Object));
		}

		public static T LoadGeneric<T>(string path) where T : Object
		{
			return (T)Load(path, typeof(T));
		}

		public static Object Load(string path, Type type)
		{
			if (string.IsNullOrEmpty(path) || ModCatalog.Mods.Count == 0)
			{
				return Resources.Load(path, type);
			}
			string key = ModCatalog.KeyFor(path + ".x");
			string cacheKey = key + "|" + type.FullName;
			if (s_Cache.TryGetValue(cacheKey, out Object cached) && cached != null)
			{
				return cached;
			}
			Object result = null;
			try
			{
				result = FromMods(path, key, type);
			}
			catch (Exception e)
			{
				Plugin.Log.LogError($"Mod asset for '{path}' failed to load: {e}");
			}
			if (result != null)
			{
				if (Plugin.LogOverrides.Value)
				{
					Plugin.Log.LogInfo($"Mod asset: {path} ({result.GetType().Name})");
				}
				s_Cache[cacheKey] = result;
				return result;
			}
			// The game's own asset (only cached when a mod changed it, so Unity can still unload the rest).
			result = Resources.Load(path, type);
			if (result is Texture2D texture && TextureOverride(texture.name) is Texture2D replacement)
			{
				s_Cache[cacheKey] = replacement;
				return replacement;
			}
			if (result is GameObject prefab)
			{
				Retexture(prefab);
			}
			return result;
		}

		private static bool Wants(Type requested, Type provided)
		{
			return requested == typeof(Object) || requested.IsAssignableFrom(provided);
		}

		private static Object FromMods(string path, string key, Type type)
		{
			if (s_BundleAssets.TryGetValue(key, out BundleAsset asset))
			{
				Object o = asset.bundle.LoadAsset(asset.name, type);
				if (o != null)
				{
					FixShaders(o);
					return o;
				}
			}
			if (Wants(type, typeof(GameObject)) && ModCatalog.Last(path + ".variant", ".txt") is string variant)
			{
				return BuildVariant(path, key, variant);
			}
			if (Wants(type, typeof(Texture2D)) && ModCatalog.Last(path, ".png", ".jpg") is string image)
			{
				return LoadTexture(image, path.StartsWith("Sprites", StringComparison.OrdinalIgnoreCase));
			}
			if (Wants(type, typeof(AudioClip)) && ModCatalog.Last(path, ".wav") is string wav)
			{
				return WavLoader.Load(wav);
			}
			if (Wants(type, typeof(TextAsset)) && ModCatalog.Last(path, ".txt") != null)
			{
				// Data files are merged by RegistryPatches; a file only a mod provides still has to
				// exist for the game's "does this wave/ability exist?" checks.
				Object original = Resources.Load(path, type);
				if (original != null)
				{
					return original;
				}
				if (s_EmptyText == null)
				{
					s_EmptyText = new TextAsset();
				}
				return s_EmptyText;
			}
			return null;
		}

		private static Texture2D LoadTexture(string file, bool sprite)
		{
			if (s_TextureFiles.TryGetValue(file, out Texture2D texture) && texture != null)
			{
				return texture;
			}
			texture = new Texture2D(2, 2, TextureFormat.RGBA32, !sprite);
			texture.LoadImage(File.ReadAllBytes(file));
			texture.name = Path.GetFileNameWithoutExtension(file);
			if (sprite)
			{
				texture.wrapMode = TextureWrapMode.Clamp;
			}
			s_TextureFiles[file] = texture;
			return texture;
		}

		private static Texture2D TextureOverride(string textureName)
		{
			if (string.IsNullOrEmpty(textureName) || !ModCatalog.TextureOverrides.TryGetValue(textureName.ToLowerInvariant(), out string file))
			{
				return null;
			}
			return LoadTexture(file, false);
		}

		// Swaps textures that mods replace by name in every material under this object.
		internal static void Retexture(GameObject root)
		{
			if (ModCatalog.TextureOverrides.Count == 0 || !s_Retextured.Add(root.GetInstanceID()))
			{
				return;
			}
			foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
			{
				foreach (Material material in renderer.sharedMaterials)
				{
					if (material == null)
					{
						continue;
					}
					foreach (string property in s_TextureProperties)
					{
						if (material.HasProperty(property) && material.GetTexture(property) is Texture current && TextureOverride(current.name) is Texture2D replacement)
						{
							material.SetTexture(property, replacement);
							if (Plugin.LogOverrides.Value)
							{
								Plugin.Log.LogInfo($"Retextured {material.name}.{property}: {current.name}");
							}
						}
					}
				}
			}
		}

		internal static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene)
		{
			if (ModCatalog.TextureOverrides.Count == 0)
			{
				return;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				Retexture(root);
			}
		}

		// Materials in bundles reference shader copies built into the bundle; use the game's own
		// shader of the same name so they render exactly like the game's materials.
		private static void FixShaders(Object o)
		{
			var go = o as GameObject;
			if (go == null)
			{
				if (o is Material m && Shader.Find(m.shader.name) is Shader s)
				{
					m.shader = s;
				}
				return;
			}
			foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>(true))
			{
				foreach (Material material in renderer.sharedMaterials)
				{
					if (material != null && Shader.Find(material.shader.name) is Shader shader)
					{
						material.shader = shader;
					}
				}
			}
		}

		// A prefab variant: "base = <prefab path>" plus optional "scale = 1.5" (or "x, y, z"),
		// "texture = file.png" (next to the variant file), "tint = r, g, b[, a]" and "model = file.glb"
		// (see AddModel). A variant may replace the prefab it's based on (same path).
		private static GameObject BuildVariant(string path, string key, string file)
		{
			SDFTreeNode def = SDFTree.LoadFromSingleString(File.ReadAllText(file));
			string basePath = def["base"];
			bool replacesBase = ModCatalog.KeyFor(basePath + ".x") == key;
			var basePrefab = (replacesBase ? Resources.Load(basePath, typeof(GameObject)) : Load(basePath, typeof(GameObject))) as GameObject;
			if (basePrefab == null)
			{
				Plugin.Log.LogError($"{file}: base prefab '{basePath}' not found");
				return null;
			}
			if (s_TemplateRoot == null)
			{
				// Templates live under an inactive object so their scripts never run; the game's
				// Instantiate() of a template produces a normal, active object.
				s_TemplateRoot = new GameObject("SvZModLoader Templates");
				s_TemplateRoot.SetActive(false);
				Object.DontDestroyOnLoad(s_TemplateRoot);
			}
			GameObject template = Object.Instantiate(basePrefab, s_TemplateRoot.transform);
			template.name = Path.GetFileName(path);
			if (def["scale"].Length > 0)
			{
				Vector3 s = ParseVector(def["scale"], 1f);
				template.transform.localScale = Vector3.Scale(template.transform.localScale, s);
			}
			Texture2D texture = def["texture"].Length > 0 ? LoadTexture(Path.Combine(Path.GetDirectoryName(file), def["texture"]), false) : null;
			Color? tint = def["tint"].Length > 0 ? (Color?)ParseColor(def["tint"]) : null;
			if (texture != null || tint.HasValue)
			{
				foreach (Renderer renderer in template.GetComponentsInChildren<Renderer>(true))
				{
					Material[] materials = renderer.sharedMaterials;
					for (int i = 0; i < materials.Length; i++)
					{
						if (materials[i] == null)
						{
							continue;
						}
						var copy = new Material(materials[i]);
						if (texture != null && copy.HasProperty("_MainTex"))
						{
							copy.SetTexture("_MainTex", texture);
						}
						if (tint.HasValue)
						{
							foreach (string property in s_ColorProperties)
							{
								if (copy.HasProperty(property))
								{
									copy.SetColor(property, copy.GetColor(property) * tint.Value);
								}
							}
						}
						materials[i] = copy;
					}
					renderer.sharedMaterials = materials;
				}
			}
			if (def["model"].Length > 0)
			{
				AddModel(template, key, def, Path.Combine(Path.GetDirectoryName(file), def["model"]));
			}
			return template;
		}

		// "model = file.glb": a glTF model shown instead of the prefab's own skinned meshes (see ModelSwap).
		// It's sized to the original's height ("modelHeight = 1.2" makes it 20% taller) and stands where
		// the original stands; models face +Z (Blender's -Y), or set "modelRotation = 0, 180, 0".
		// "modelOffset = x, y, z" moves it (in the original's units). The model moves in one of two ways:
		//   [bones]   original bone = model bone  - retargeting: the model bone copies the original
		//             bone's motion ("alignRest = no" keeps differing rest poses as they are)
		//   follow = <original bone>              - the model moves with that bone and is centred on it,
		//             playing its own animations: [animations] game animation = model animation
		// Other sections:
		//   [attach]  original bone = model bone  - the original bone (and whatever the game attached
		//                                           to it, like a weapon) is moved onto the model bone
		//   [clips]   new name = existing name    - adds a copy of one of the original's animations
		//                                           under another name (e.g. a ranged attack)
		//   [laser]   from, color, width, duration - the character's bullets become laser beams fired
		//                                           from model bone "from" (see ProjectilePatches)
		private static void AddModel(GameObject template, string key, SDFTreeNode def, string file)
		{
			var originals = new List<Renderer>(template.GetComponentsInChildren<SkinnedMeshRenderer>(true));
			GameObject model = Object.Instantiate(LoadModel(file, originals));
			model.name = ModelSwap.ModelPrefix + key;
			Transform t = model.transform;
			t.SetParent(template.transform, false);
			if (def["modelRotation"].Length > 0)
			{
				t.localRotation = Quaternion.Euler(ParseVector(def["modelRotation"], 0f));
			}
			var modelRenderers = new List<Renderer>(model.GetComponentsInChildren<Renderer>(true));
			Bounds original = LocalBounds(template.transform, originals);
			Bounds bounds = LocalBounds(template.transform, modelRenderers);
			float height = def["modelHeight"].Length > 0 ? ParseFloats(def["modelHeight"])[0] : 1f;
			if (original.size.y > 0f && bounds.size.y > 0f)
			{
				t.localScale = Vector3.one * (original.size.y / bounds.size.y * height);
				bounds = LocalBounds(template.transform, modelRenderers);
			}
			var config = new ModelSwap.Config { Follow = def["follow"], AlignRest = Flag(def["alignRest"], true) };
			Transform follow = config.Follow.Length > 0 ? ModelSwap.Find(ModelSwap.BonesByName(template.transform, t), config.Follow) : null;
			if (follow != null)
			{
				t.localPosition += template.transform.InverseTransformPoint(follow.position) - bounds.center;
			}
			else
			{
				t.localPosition += new Vector3(original.center.x - bounds.center.x, original.min.y - bounds.min.y, original.center.z - bounds.center.z);
			}
			if (def["modelOffset"].Length > 0)
			{
				t.localPosition += ParseVector(def["modelOffset"], 0f);
			}

			foreach (Renderer renderer in originals)
			{
				renderer.enabled = false;
			}
			// The hidden skeleton must keep animating even though nothing of it is drawn.
			foreach (Animation animation in template.GetComponentsInChildren<Animation>(true))
			{
				animation.cullingType = AnimationCullingType.AlwaysAnimate;
			}
			AddClips(template, t, def);
			ReadPairs(def, "bones", config.Bones);
			ReadPairs(def, "attach", config.Attach);
			if (def.hasChild("laser"))
			{
				SDFTreeNode laser = def.to("laser");
				config.Laser = new LaserBeam.Settings { From = laser["from"] };
				if (laser["color"].Length > 0)
				{
					config.Laser.Color = ParseColor(laser["color"]);
				}
				if (laser["width"].Length > 0)
				{
					config.Laser.Width = ParseFloats(laser["width"])[0];
				}
				if (laser["duration"].Length > 0)
				{
					config.Laser.Duration = ParseFloats(laser["duration"])[0];
				}
			}
			ModelSwap.Configs[model.name] = config;
			template.AddComponent<ModelSwap>();
			string moves = follow != null ? "following " + follow.name : config.Bones.Count + " bones mapped";
			Plugin.Log.LogInfo($"Model {Path.GetFileName(file)} replaces {template.name} ({modelRenderers.Count} meshes, {moves}{(config.Laser != null ? ", fires lasers" : "")})");
		}

		// Each .glb is read once; every variant using it gets a copy (sharing meshes and textures).
		// Its materials are copies of the first original's material, so it uses the game's shader.
		private static GameObject LoadModel(string file, List<Renderer> originals)
		{
			string fullPath = Path.GetFullPath(file);
			if (s_Models.TryGetValue(fullPath, out GameObject loaded) && loaded != null)
			{
				return loaded;
			}
			Material material = null;
			foreach (Renderer renderer in originals)
			{
				if (renderer.sharedMaterial != null)
				{
					material = renderer.sharedMaterial;
					break;
				}
			}
			loaded = GltfLoader.Load(fullPath, material != null ? material : new Material(Shader.Find("Diffuse")));
			loaded.transform.SetParent(s_TemplateRoot.transform, false);
			s_Models[fullPath] = loaded;
			return loaded;
		}

		// [animations] gives the model's own clips the names the game plays them by (the game plays each
		// animation on every Animation component under the character, skipping ones that lack it);
		// [clips] adds copies of the original's clips under new names.
		private static void AddClips(GameObject template, Transform model, SDFTreeNode def)
		{
			var pairs = new List<string[]>();
			ReadPairs(def, "animations", pairs);
			var player = model.GetComponent<Animation>();
			foreach (string[] pair in pairs)
			{
				AnimationClip clip = player != null ? player.GetClip(pair[1]) : null;
				if (clip == null)
				{
					Plugin.Log.LogWarning($"{template.name}: the model has no animation '{pair[1]}' (for '{pair[0]}')");
					continue;
				}
				player.AddClip(clip, pair[0]);
			}
			pairs.Clear();
			ReadPairs(def, "clips", pairs);
			foreach (Animation animation in template.GetComponentsInChildren<Animation>(true))
			{
				if (animation.transform.IsChildOf(model))
				{
					continue;
				}
				foreach (string[] pair in pairs)
				{
					AnimationClip clip = animation.GetClip(pair[1]);
					if (clip != null)
					{
						animation.AddClip(clip, pair[0]);
					}
				}
			}
		}

		private static bool Flag(string value, bool fallback)
		{
			return value.Length > 0 ? SvZModding.ModLayout.ParseBool(value, fallback) : fallback;
		}

		private static void ReadPairs(SDFTreeNode def, string section, List<string[]> pairs)
		{
			if (!def.hasChild(section))
			{
				return;
			}
			foreach (KeyValuePair<string, string> pair in def.to(section).attributes)
			{
				pairs.Add(new[] { pair.Key, pair.Value });
			}
		}

		// Bounds of renderers' meshes as posed now (a template's rest pose), in the space of `space`.
		// Skinned meshes are skinned here on the CPU: their vertex data can be stored in any space
		// (the hero's is Maya's Z-up), and only bones x bind poses say where vertices really are.
		private static Bounds LocalBounds(Transform space, List<Renderer> renderers)
		{
			bool any = false;
			var result = new Bounds();
			Action<Vector3> add = p =>
			{
				if (!any)
				{
					result = new Bounds(p, Vector3.zero);
					any = true;
				}
				else
				{
					result.Encapsulate(p);
				}
			};
			foreach (Renderer renderer in renderers)
			{
				var skinned = renderer as SkinnedMeshRenderer;
				Mesh mesh = skinned != null ? skinned.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
				if (mesh == null)
				{
					continue;
				}
				Vector3[] vertices = mesh.vertices;
				BoneWeight[] weights = skinned != null ? mesh.boneWeights : null;
				if (weights == null || weights.Length != vertices.Length)
				{
					Matrix4x4 toSpace = space.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
					foreach (Vector3 v in vertices)
					{
						add(toSpace.MultiplyPoint3x4(v));
					}
					continue;
				}
				Transform[] bones = skinned.bones;
				Matrix4x4[] bindposes = mesh.bindposes;
				var skin = new Matrix4x4[bones.Length];
				for (int b = 0; b < bones.Length; b++)
				{
					skin[b] = bones[b] != null && b < bindposes.Length ? space.worldToLocalMatrix * bones[b].localToWorldMatrix * bindposes[b] : Matrix4x4.zero;
				}
				for (int i = 0; i < vertices.Length; i++)
				{
					BoneWeight w = weights[i];
					Vector3 v = vertices[i];
					add(skin[w.boneIndex0].MultiplyPoint3x4(v) * w.weight0 + skin[w.boneIndex1].MultiplyPoint3x4(v) * w.weight1
						+ skin[w.boneIndex2].MultiplyPoint3x4(v) * w.weight2 + skin[w.boneIndex3].MultiplyPoint3x4(v) * w.weight3);
				}
			}
			return result;
		}

		private static float[] ParseFloats(string text)
		{
			string[] parts = text.Split(',');
			var values = new float[parts.Length];
			for (int i = 0; i < parts.Length; i++)
			{
				values[i] = float.Parse(parts[i].Trim(), System.Globalization.CultureInfo.InvariantCulture);
			}
			return values;
		}

		private static Vector3 ParseVector(string text, float fill)
		{
			float[] v = ParseFloats(text);
			return v.Length == 1 ? new Vector3(v[0], v[0], v[0]) : new Vector3(v[0], v.Length > 1 ? v[1] : fill, v.Length > 2 ? v[2] : fill);
		}

		private static Color ParseColor(string text)
		{
			float[] v = ParseFloats(text);
			return new Color(v[0], v.Length > 1 ? v[1] : v[0], v.Length > 2 ? v[2] : v[0], v.Length > 3 ? v[3] : 1f);
		}
	}
}
