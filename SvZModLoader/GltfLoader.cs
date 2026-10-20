using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SvZModLoader
{
	// Loads a binary glTF (.glb, e.g. exported from Blender with File > Export > glTF 2.0) as a
	// GameObject hierarchy: its nodes, meshes (rigged ones become SkinnedMeshRenderers bound to the
	// node bones), base-color textures and animations (legacy clips on an Animation component at the
	// root, named like the glTF animations - Blender's actions or NLA tracks). Materials are copies of a game material, so the model is
	// drawn with the game's own shader. glTF's right-handed axes are converted to Unity's by mirroring
	// X, so a model facing +Z in glTF (Blender's -Y) faces +Z in the game too.
	internal static class GltfLoader
	{
		private class Context
		{
			public Dictionary<string, object> Json;
			public byte[] Bin;
			public Transform Root;
			public Transform[] Nodes;
			public Material Template;
			public readonly Dictionary<int, Texture2D> Images = new Dictionary<int, Texture2D>();
			public readonly Dictionary<int, Material> Materials = new Dictionary<int, Material>();
		}

		public static GameObject Load(string file, Material template)
		{
			byte[] data = File.ReadAllBytes(file);
			if (data.Length < 20 || BitConverter.ToUInt32(data, 0) != 0x46546C67)
			{
				throw new Exception("not a binary glTF (.glb) file");
			}
			var ctx = new Context { Template = template };
			int offset = 12;
			while (offset + 8 <= data.Length)
			{
				int length = BitConverter.ToInt32(data, offset);
				uint type = BitConverter.ToUInt32(data, offset + 4);
				if (type == 0x4E4F534A)
				{
					ctx.Json = (Dictionary<string, object>)MiniJson.Parse(Encoding.UTF8.GetString(data, offset + 8, length));
				}
				else if (type == 0x004E4942)
				{
					ctx.Bin = new byte[length];
					Buffer.BlockCopy(data, offset + 8, ctx.Bin, 0, length);
				}
				offset += 8 + length;
			}

			var root = new GameObject(Path.GetFileNameWithoutExtension(file));
			ctx.Root = root.transform;
			List<object> nodes = List(ctx.Json, "nodes");
			ctx.Nodes = new Transform[nodes.Count];
			for (int i = 0; i < nodes.Count; i++)
			{
				var node = (Dictionary<string, object>)nodes[i];
				ctx.Nodes[i] = new GameObject(node.ContainsKey("name") ? (string)node["name"] : "node" + i).transform;
			}
			for (int i = 0; i < nodes.Count; i++)
			{
				var node = (Dictionary<string, object>)nodes[i];
				foreach (object child in List(node, "children"))
				{
					ctx.Nodes[Int(child)].SetParent(ctx.Nodes[i], false);
				}
			}
			var scenes = List(ctx.Json, "scenes");
			int scene = ctx.Json.ContainsKey("scene") ? Int(ctx.Json["scene"]) : 0;
			foreach (object index in List((Dictionary<string, object>)scenes[scene], "nodes"))
			{
				ctx.Nodes[Int(index)].SetParent(root.transform, false);
			}
			for (int i = 0; i < nodes.Count; i++)
			{
				SetTransform(ctx.Nodes[i], (Dictionary<string, object>)nodes[i]);
			}
			// Meshes last: without inverse bind matrices, bind poses come from the finished hierarchy.
			for (int i = 0; i < nodes.Count; i++)
			{
				var node = (Dictionary<string, object>)nodes[i];
				if (node.ContainsKey("mesh"))
				{
					AddMesh(ctx, ctx.Nodes[i], Int(node["mesh"]), node.ContainsKey("skin") ? Int(node["skin"]) : -1);
				}
			}
			ImportAnimations(ctx);
			return root;
		}

		private static List<object> List(Dictionary<string, object> obj, string key)
		{
			return obj.TryGetValue(key, out object value) ? (List<object>)value : new List<object>();
		}

		private static Dictionary<string, object> Obj(Dictionary<string, object> obj, string key)
		{
			return obj.TryGetValue(key, out object value) ? value as Dictionary<string, object> : null;
		}

		private static int Int(object value)
		{
			return (int)(double)value;
		}

		private static float F(List<object> list, int i)
		{
			return (float)(double)list[i];
		}

		private static void SetTransform(Transform t, Dictionary<string, object> node)
		{
			if (node.ContainsKey("matrix"))
			{
				List<object> m = List(node, "matrix");
				var matrix = new Matrix4x4();
				for (int i = 0; i < 16; i++)
				{
					matrix[i % 4, i / 4] = F(m, i); // glTF matrices are column-major
				}
				Matrix4x4 mirror = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
				matrix = mirror * matrix * mirror;
				t.localPosition = matrix.GetColumn(3);
				t.localRotation = Quaternion.LookRotation(matrix.GetColumn(2), matrix.GetColumn(1));
				t.localScale = new Vector3(matrix.GetColumn(0).magnitude, matrix.GetColumn(1).magnitude, matrix.GetColumn(2).magnitude);
				return;
			}
			List<object> p = List(node, "translation");
			if (p.Count == 3)
			{
				t.localPosition = new Vector3(-F(p, 0), F(p, 1), F(p, 2));
			}
			List<object> r = List(node, "rotation");
			if (r.Count == 4)
			{
				t.localRotation = new Quaternion(F(r, 0), -F(r, 1), -F(r, 2), F(r, 3));
			}
			List<object> s = List(node, "scale");
			if (s.Count == 3)
			{
				t.localScale = new Vector3(F(s, 0), F(s, 1), F(s, 2));
			}
		}

		// Accessor data as floats (normalized integers are scaled to 0..1), with its component count.
		private static float[] ReadFloats(Context ctx, int accessorIndex, out int components)
		{
			var accessor = (Dictionary<string, object>)List(ctx.Json, "accessors")[accessorIndex];
			components = Components((string)accessor["type"]);
			int count = Int(accessor["count"]);
			int componentType = Int(accessor["componentType"]);
			bool normalized = accessor.ContainsKey("normalized") && (bool)accessor["normalized"];
			int size = ComponentSize(componentType);
			var view = (Dictionary<string, object>)List(ctx.Json, "bufferViews")[Int(accessor["bufferView"])];
			int start = (view.ContainsKey("byteOffset") ? Int(view["byteOffset"]) : 0) + (accessor.ContainsKey("byteOffset") ? Int(accessor["byteOffset"]) : 0);
			int stride = view.ContainsKey("byteStride") ? Int(view["byteStride"]) : size * components;
			var values = new float[count * components];
			for (int i = 0; i < count; i++)
			{
				for (int c = 0; c < components; c++)
				{
					int at = start + i * stride + c * size;
					float v;
					switch (componentType)
					{
					case 5126: v = BitConverter.ToSingle(ctx.Bin, at); break;
					case 5121: v = ctx.Bin[at]; if (normalized) v /= 255f; break;
					case 5123: v = BitConverter.ToUInt16(ctx.Bin, at); if (normalized) v /= 65535f; break;
					case 5125: v = BitConverter.ToUInt32(ctx.Bin, at); break;
					case 5120: v = (sbyte)ctx.Bin[at]; if (normalized) v = Mathf.Max(v / 127f, -1f); break;
					case 5122: v = BitConverter.ToInt16(ctx.Bin, at); if (normalized) v = Mathf.Max(v / 32767f, -1f); break;
					default: throw new Exception("unsupported accessor component type " + componentType);
					}
					values[i * components + c] = v;
				}
			}
			return values;
		}

		private static int Components(string type)
		{
			switch (type)
			{
			case "SCALAR": return 1;
			case "VEC2": return 2;
			case "VEC3": return 3;
			case "VEC4": return 4;
			case "MAT4": return 16;
			default: throw new Exception("unsupported accessor type " + type);
			}
		}

		private static int ComponentSize(int componentType)
		{
			switch (componentType)
			{
			case 5120: case 5121: return 1;
			case 5122: case 5123: return 2;
			default: return 4;
			}
		}

		private static void AddMesh(Context ctx, Transform node, int meshIndex, int skinIndex)
		{
			var gltfMesh = (Dictionary<string, object>)List(ctx.Json, "meshes")[meshIndex];
			List<object> primitives = List(gltfMesh, "primitives");
			for (int p = 0; p < primitives.Count; p++)
			{
				var primitive = (Dictionary<string, object>)primitives[p];
				// A skinned mesh's vertices are in the scene's space (glTF ignores its node's transform), so
				// its renderer sits at the model root; other meshes go on their node.
				Transform target = node;
				if (skinIndex >= 0)
				{
					target = new GameObject(node.name + (primitives.Count > 1 ? "_" + p : "")).transform;
					target.SetParent(ctx.Root, false);
				}
				else if (primitives.Count > 1)
				{
					target = new GameObject(node.name + "_" + p).transform;
					target.SetParent(node, false);
				}
				Mesh mesh = BuildMesh(ctx, primitive, target.name);
				Material material = primitive.ContainsKey("material") ? GetMaterial(ctx, Int(primitive["material"])) : ctx.Template;
				if (skinIndex >= 0 && mesh.boneWeights.Length > 0)
				{
					var skin = (Dictionary<string, object>)List(ctx.Json, "skins")[skinIndex];
					List<object> joints = List(skin, "joints");
					var bones = new Transform[joints.Count];
					var bindposes = new Matrix4x4[joints.Count];
					float[] inverseBinds = skin.ContainsKey("inverseBindMatrices") ? ReadFloats(ctx, Int(skin["inverseBindMatrices"]), out int _) : null;
					Matrix4x4 mirror = Matrix4x4.Scale(new Vector3(-1f, 1f, 1f));
					for (int j = 0; j < joints.Count; j++)
					{
						bones[j] = ctx.Nodes[Int(joints[j])];
						if (inverseBinds != null)
						{
							var m = new Matrix4x4();
							for (int k = 0; k < 16; k++)
							{
								m[k % 4, k / 4] = inverseBinds[j * 16 + k];
							}
							bindposes[j] = mirror * m * mirror;
						}
						else
						{
							bindposes[j] = bones[j].worldToLocalMatrix * target.localToWorldMatrix;
						}
					}
					mesh.bindposes = bindposes;
					var renderer = target.gameObject.AddComponent<SkinnedMeshRenderer>();
					renderer.sharedMesh = mesh;
					renderer.bones = bones;
					renderer.rootBone = skin.ContainsKey("skeleton") ? ctx.Nodes[Int(skin["skeleton"])] : bones[0];
					renderer.quality = SkinQuality.Bone4;
					renderer.updateWhenOffscreen = true;
					renderer.sharedMaterial = material;
				}
				else
				{
					target.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
					target.gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
				}
			}
		}

		private static Mesh BuildMesh(Context ctx, Dictionary<string, object> primitive, string name)
		{
			var attributes = Obj(primitive, "attributes");
			float[] positions = ReadFloats(ctx, Int(attributes["POSITION"]), out int _);
			int count = positions.Length / 3;
			var mesh = new Mesh { name = name };
			if (count > 65535)
			{
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			}
			var vertices = new Vector3[count];
			for (int i = 0; i < count; i++)
			{
				vertices[i] = new Vector3(-positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
			}
			mesh.vertices = vertices;
			if (attributes.ContainsKey("NORMAL"))
			{
				float[] n = ReadFloats(ctx, Int(attributes["NORMAL"]), out int _);
				var normals = new Vector3[count];
				for (int i = 0; i < count; i++)
				{
					normals[i] = new Vector3(-n[i * 3], n[i * 3 + 1], n[i * 3 + 2]);
				}
				mesh.normals = normals;
			}
			if (attributes.ContainsKey("TEXCOORD_0"))
			{
				float[] t = ReadFloats(ctx, Int(attributes["TEXCOORD_0"]), out int _);
				var uv = new Vector2[count];
				for (int i = 0; i < count; i++)
				{
					uv[i] = new Vector2(t[i * 2], 1f - t[i * 2 + 1]);
				}
				mesh.uv = uv;
			}
			if (attributes.ContainsKey("JOINTS_0") && attributes.ContainsKey("WEIGHTS_0"))
			{
				float[] j = ReadFloats(ctx, Int(attributes["JOINTS_0"]), out int _);
				float[] w = ReadFloats(ctx, Int(attributes["WEIGHTS_0"]), out int _);
				var weights = new BoneWeight[count];
				for (int i = 0; i < count; i++)
				{
					float sum = w[i * 4] + w[i * 4 + 1] + w[i * 4 + 2] + w[i * 4 + 3];
					float scale = sum > 0f ? 1f / sum : 0f;
					weights[i] = new BoneWeight
					{
						boneIndex0 = (int)j[i * 4], weight0 = w[i * 4] * scale,
						boneIndex1 = (int)j[i * 4 + 1], weight1 = w[i * 4 + 1] * scale,
						boneIndex2 = (int)j[i * 4 + 2], weight2 = w[i * 4 + 2] * scale,
						boneIndex3 = (int)j[i * 4 + 3], weight3 = w[i * 4 + 3] * scale,
					};
				}
				mesh.boneWeights = weights;
			}
			int[] triangles;
			if (primitive.ContainsKey("indices"))
			{
				float[] idx = ReadFloats(ctx, Int(primitive["indices"]), out int _);
				triangles = new int[idx.Length];
				for (int i = 0; i < idx.Length; i++)
				{
					triangles[i] = (int)idx[i];
				}
			}
			else
			{
				triangles = new int[count];
				for (int i = 0; i < count; i++)
				{
					triangles[i] = i;
				}
			}
			// Mirroring X flips the winding; swap two corners of each triangle to keep faces outward.
			for (int i = 0; i + 2 < triangles.Length; i += 3)
			{
				int swap = triangles[i + 1];
				triangles[i + 1] = triangles[i + 2];
				triangles[i + 2] = swap;
			}
			mesh.triangles = triangles;
			if (!attributes.ContainsKey("NORMAL"))
			{
				mesh.RecalculateNormals();
			}
			mesh.RecalculateBounds();
			return mesh;
		}

		private static void ImportAnimations(Context ctx)
		{
			List<object> animations = List(ctx.Json, "animations");
			if (animations.Count == 0)
			{
				return;
			}
			var player = ctx.Root.gameObject.AddComponent<Animation>();
			player.playAutomatically = false;
			player.cullingType = AnimationCullingType.AlwaysAnimate;
			for (int a = 0; a < animations.Count; a++)
			{
				var animation = (Dictionary<string, object>)animations[a];
				var clip = new AnimationClip { legacy = true, name = animation.ContainsKey("name") ? (string)animation["name"] : "animation" + a };
				List<object> samplers = List(animation, "samplers");
				foreach (object c in List(animation, "channels"))
				{
					var channel = (Dictionary<string, object>)c;
					var target = Obj(channel, "target");
					if (target == null || !target.ContainsKey("node"))
					{
						continue;
					}
					var sampler = (Dictionary<string, object>)samplers[Int(channel["sampler"])];
					string path = PathFromRoot(ctx, ctx.Nodes[Int(target["node"])]);
					string interpolation = sampler.ContainsKey("interpolation") ? (string)sampler["interpolation"] : "LINEAR";
					float[] times = ReadFloats(ctx, Int(sampler["input"]), out int _);
					float[] values = ReadFloats(ctx, Int(sampler["output"]), out int components);
					switch ((string)target["path"])
					{
					case "translation":
						AddCurves(clip, path, "localPosition", new[] { "x", "y", "z" }, new[] { -1f, 1f, 1f }, times, values, components, interpolation);
						break;
					case "rotation":
						MakeContinuous(values, interpolation);
						AddCurves(clip, path, "localRotation", new[] { "x", "y", "z", "w" }, new[] { 1f, -1f, -1f, 1f }, times, values, components, interpolation);
						break;
					case "scale":
						AddCurves(clip, path, "localScale", new[] { "x", "y", "z" }, new[] { 1f, 1f, 1f }, times, values, components, interpolation);
						break;
					}
				}
				clip.EnsureQuaternionContinuity();
				player.AddClip(clip, clip.name);
			}
		}

		private static string PathFromRoot(Context ctx, Transform node)
		{
			string path = node.name;
			for (Transform t = node.parent; t != null && t != ctx.Root; t = t.parent)
			{
				path = t.name + "/" + path;
			}
			return path;
		}

		// Neighbouring rotation keys on opposite sides of the quaternion sphere would interpolate the
		// long way round; flip them onto the same side.
		private static void MakeContinuous(float[] q, string interpolation)
		{
			if (interpolation == "CUBICSPLINE")
			{
				return;
			}
			for (int k = 4; k + 3 < q.Length; k += 4)
			{
				if (q[k] * q[k - 4] + q[k + 1] * q[k - 3] + q[k + 2] * q[k - 2] + q[k + 3] * q[k - 1] < 0f)
				{
					for (int i = 0; i < 4; i++)
					{
						q[k + i] = -q[k + i];
					}
				}
			}
		}

		// One curve per component (with glTF -> Unity sign flips). Linear keys get straight tangents,
		// step keys infinite ones; cubic-spline keys bring their own (in value per second, like Unity's).
		private static void AddCurves(AnimationClip clip, string path, string property, string[] axes, float[] signs, float[] times, float[] values, int components, string interpolation)
		{
			bool cubic = interpolation == "CUBICSPLINE";
			bool step = interpolation == "STEP";
			for (int c = 0; c < axes.Length && c < components; c++)
			{
				var keys = new Keyframe[times.Length];
				for (int k = 0; k < times.Length; k++)
				{
					float value = signs[c] * values[((cubic ? 3 * k + 1 : k) * components) + c];
					keys[k] = new Keyframe(times[k], value);
				}
				for (int k = 0; k < keys.Length; k++)
				{
					if (cubic)
					{
						keys[k].inTangent = signs[c] * values[(3 * k * components) + c];
						keys[k].outTangent = signs[c] * values[((3 * k + 2) * components) + c];
					}
					else if (step)
					{
						keys[k].inTangent = float.PositiveInfinity;
						keys[k].outTangent = float.PositiveInfinity;
					}
					else
					{
						keys[k].inTangent = k > 0 ? (keys[k].value - keys[k - 1].value) / Mathf.Max(1e-5f, keys[k].time - keys[k - 1].time) : 0f;
						keys[k].outTangent = k + 1 < keys.Length ? (keys[k + 1].value - keys[k].value) / Mathf.Max(1e-5f, keys[k + 1].time - keys[k].time) : 0f;
					}
				}
				clip.SetCurve(path, typeof(Transform), property + "." + axes[c], new AnimationCurve(keys));
			}
		}

		private static Material GetMaterial(Context ctx, int index)
		{
			if (ctx.Materials.TryGetValue(index, out Material material))
			{
				return material;
			}
			var gltfMaterial = (Dictionary<string, object>)List(ctx.Json, "materials")[index];
			material = new Material(ctx.Template) { name = gltfMaterial.ContainsKey("name") ? (string)gltfMaterial["name"] : "material" + index };
			var pbr = Obj(gltfMaterial, "pbrMetallicRoughness");
			var baseColor = pbr != null ? Obj(pbr, "baseColorTexture") : null;
			if (baseColor != null && material.HasProperty("_MainTex"))
			{
				var texture = (Dictionary<string, object>)List(ctx.Json, "textures")[Int(baseColor["index"])];
				material.SetTexture("_MainTex", GetImage(ctx, Int(texture["source"])));
				material.SetTextureScale("_MainTex", Vector2.one);
				material.SetTextureOffset("_MainTex", Vector2.zero);
			}
			ctx.Materials[index] = material;
			return material;
		}

		private static Texture2D GetImage(Context ctx, int index)
		{
			if (ctx.Images.TryGetValue(index, out Texture2D texture))
			{
				return texture;
			}
			var image = (Dictionary<string, object>)List(ctx.Json, "images")[index];
			var view = (Dictionary<string, object>)List(ctx.Json, "bufferViews")[Int(image["bufferView"])];
			int start = view.ContainsKey("byteOffset") ? Int(view["byteOffset"]) : 0;
			var bytes = new byte[Int(view["byteLength"])];
			Buffer.BlockCopy(ctx.Bin, start, bytes, 0, bytes.Length);
			texture = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = image.ContainsKey("name") ? (string)image["name"] : "image" + index };
			texture.LoadImage(bytes);
			ctx.Images[index] = texture;
			return texture;
		}
	}
}
