using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SvZModding;

namespace SvZModManager
{
	// One mod folder as the manager shows it: its mod.txt, its files by category, and the player's
	// choices for it.
	internal class ModEntry
	{
		public string Folder;
		public ModManifest Manifest;
		public bool Enabled;
		public readonly HashSet<ModCategory> Off = new HashSet<ModCategory>();

		// Category -> files (relative to the mod folder, '/' separated).
		public readonly Dictionary<ModCategory, List<string>> Files = new Dictionary<ModCategory, List<string>>();

		public string FolderName => Path.GetFileName(Folder);

		public int Count(ModCategory category)
		{
			return Files.TryGetValue(category, out List<string> list) ? list.Count : 0;
		}

		public bool Uses(ModCategory category)
		{
			return Enabled && !Off.Contains(category) && Count(category) > 0;
		}

		public string Summary
		{
			get
			{
				string[] names = ModLayout.Categories.Where(c => Count(c) > 0).Select(c => c.ToString()).ToArray();
				return names.Length > 0 ? string.Join(", ", names) : "nothing the loader uses";
			}
		}

		// What the game asks the loader for, so two mods' files can be compared: Resources paths
		// without extension (Sprites/a.png and Sprites/a.jpg both answer "sprites/a"), texture names.
		public static string GameKey(string relative)
		{
			string p = relative.ToLowerInvariant();
			if (p.StartsWith("resources/"))
			{
				p = p.Substring("resources/".Length);
				if (!p.EndsWith(".variant.txt"))
				{
					p = Path.ChangeExtension(p, null);
				}
				return p;
			}
			if (p.StartsWith("textures/"))
			{
				return "texture:" + Path.GetFileNameWithoutExtension(p);
			}
			return null;
		}

		// All mods in load order: the saved selection's order first, then mods added since (by their
		// mod.txt priority, then name) - the same order the in-game loader uses.
		public static List<ModEntry> Scan(string modsRoot)
		{
			var found = new List<ModEntry>();
			if (!Directory.Exists(modsRoot))
			{
				return found;
			}
			foreach (string dir in Directory.GetDirectories(modsRoot))
			{
				var mod = new ModEntry { Folder = dir, Manifest = ModManifest.Read(dir) };
				mod.Enabled = mod.Manifest.Enabled;
				foreach (string file in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
				{
					string relative = Path.GetRelativePath(dir, file).Replace('\\', '/');
					ModCategory? category = ModLayout.Classify(relative);
					if (category.HasValue)
					{
						if (!mod.Files.TryGetValue(category.Value, out List<string> list))
						{
							mod.Files[category.Value] = list = new List<string>();
						}
						list.Add(relative);
					}
				}
				found.Add(mod);
			}
			found.Sort((a, b) => a.Manifest.Priority != b.Manifest.Priority ? a.Manifest.Priority.CompareTo(b.Manifest.Priority) : string.Compare(a.Manifest.Name, b.Manifest.Name, StringComparison.OrdinalIgnoreCase));
			ModSelection selection = ModSelection.Read(modsRoot);
			if (selection == null)
			{
				return found;
			}
			var ordered = new List<ModEntry>();
			foreach (ModSelection.Entry entry in selection.Entries)
			{
				ModEntry mod = found.Find(m => string.Equals(m.FolderName, entry.Folder, StringComparison.OrdinalIgnoreCase));
				if (mod != null)
				{
					mod.Enabled = entry.Enabled;
					mod.Off.UnionWith(entry.Off);
					ordered.Add(mod);
					found.Remove(mod);
				}
			}
			ordered.AddRange(found);
			return ordered;
		}

		public static void Save(string modsRoot, IEnumerable<ModEntry> mods)
		{
			var selection = new ModSelection();
			foreach (ModEntry mod in mods)
			{
				var entry = new ModSelection.Entry { Folder = mod.FolderName, Enabled = mod.Enabled };
				entry.Off.UnionWith(mod.Off);
				selection.Entries.Add(entry);
			}
			selection.Write(modsRoot);
		}
	}
}
