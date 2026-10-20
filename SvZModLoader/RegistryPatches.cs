using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SvZModLoader
{
	// The game reads all its data (enemies, waves, abilities, helpers, store, text...) from SDF text
	// files through SDFTree.LoadFromResources. After the original file is parsed, each mod's file at
	// the same path (Mods/<mod>/Resources/<path>.txt) is merged into it:
	//  - "key = value" lines override (or add) values,
	//  - [sections] merge recursively; new sections are added,
	//  - bare list entries (e.g. the spawn list in a wave's [Commands]) are appended,
	//  - "_replace = yes" inside a section replaces that section instead of merging,
	//  - "_inherit = OtherSection" on a new section starts it as a copy of a sibling section.
	// A mod can also add files that don't exist in the game (a new ability, a new wave).
	[HarmonyPatch]
	internal static class RegistryPatches
	{
		private static readonly Dictionary<string, SDFTreeNode> s_Parsed = new Dictionary<string, SDFTreeNode>();
		private static readonly HashSet<string> s_Dumped = new HashSet<string>();
		private static readonly HashSet<string> s_Logged = new HashSet<string>();

		[HarmonyPatch(typeof(SDFTree), nameof(SDFTree.LoadFromResources))]
		[HarmonyPrefix]
		private static void DumpOriginal(string filename)
		{
			if (!Plugin.DumpData.Value || !s_Dumped.Add(filename.ToLowerInvariant()))
			{
				return;
			}
			var original = Resources.Load(filename, typeof(TextAsset)) as TextAsset;
			if (original != null)
			{
				string path = Path.Combine(Plugin.DumpFolder, "Resources/" + filename + ".txt");
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, original.text);
			}
		}

		[HarmonyPatch(typeof(SDFTree), nameof(SDFTree.LoadFromResources))]
		[HarmonyPostfix]
		private static void MergeMods(string filename, ref SDFTreeNode __result)
		{
			foreach (string file in ModCatalog.Files(filename, ".txt"))
			{
				SDFTreeNode mod = Parse(file);
				if (mod == null)
				{
					continue;
				}
				if (__result == null)
				{
					__result = new SDFTreeNode();
				}
				Merge(__result, mod);
				if (Plugin.LogOverrides.Value && s_Logged.Add(file))
				{
					Plugin.Log.LogInfo($"Merged {Relative(file)} into {filename}");
				}
			}
		}

		private static SDFTreeNode Parse(string file)
		{
			if (!s_Parsed.TryGetValue(file, out SDFTreeNode tree))
			{
				try
				{
					tree = SDFTree.LoadFromSingleString(File.ReadAllText(file));
				}
				catch (Exception e)
				{
					Plugin.Log.LogError($"Couldn't parse {Relative(file)}: {e.Message}");
					tree = null;
				}
				s_Parsed[file] = tree;
			}
			return tree;
		}

		private static bool IsListKey(string key)
		{
			return key.Length > 0 && key.All(char.IsDigit);
		}

		private static bool Flag(SDFTreeNode node, string key)
		{
			string v = node[key];
			return v.Equals("yes", StringComparison.OrdinalIgnoreCase) || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1";
		}

		internal static void Merge(SDFTreeNode target, SDFTreeNode mod)
		{
			if (Flag(mod, "_replace"))
			{
				target.ClearAttributes();
				target.ClearChilds();
			}
			int nextListIndex = target.attributes.Cast<KeyValuePair<string, string>>()
				.Where(a => IsListKey(a.Key)).Select(a => int.Parse(a.Key) + 1).DefaultIfEmpty(0).Max();
			foreach (KeyValuePair<string, string> attribute in mod.attributes)
			{
				if (attribute.Key.StartsWith("_"))
				{
					continue;
				}
				if (IsListKey(attribute.Key))
				{
					target[nextListIndex++] = attribute.Value;
				}
				else
				{
					target[attribute.Key] = attribute.Value;
				}
			}
			foreach (KeyValuePair<string, SDFTreeNode> child in mod.childs)
			{
				if (target.hasChild(child.Key) && !Flag(child.Value, "_replace"))
				{
					Merge(target.to(child.Key), child.Value);
					continue;
				}
				string inherit = child.Value["_inherit"];
				SDFTreeNode start = inherit.Length > 0 && target.hasChild(inherit) ? target.to(inherit).Clone() : new SDFTreeNode();
				Merge(start, child.Value);
				target.SetChild(child.Key, start);
			}
		}

		internal static string Relative(string file)
		{
			string root = Path.GetDirectoryName(Path.GetDirectoryName(ModCatalog.Mods.Count > 0 ? ModCatalog.Mods[0].Folder : file));
			return file.StartsWith(root) ? file.Substring(root.Length + 1) : file;
		}
	}
}
