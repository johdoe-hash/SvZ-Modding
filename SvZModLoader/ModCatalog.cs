using System;
using System.Collections.Generic;
using System.IO;
using SvZModding;

namespace SvZModLoader
{
	// One installed mod: <Mods>/<Folder>/ with an optional mod.txt, plus the mod manager's choices for it.
	internal class ModInfo
	{
		public readonly string Folder;
		public readonly ModManifest Manifest;
		public ModSelection.Entry Choice;

		public ModInfo(string folder)
		{
			Folder = folder;
			Manifest = ModManifest.Read(folder);
		}

		public string Name => Manifest.Name;

		public bool Enabled => Choice != null ? Choice.Enabled : Manifest.Enabled;

		public bool Allows(ModCategory category)
		{
			return Enabled && (Choice == null || Choice.Allows(category));
		}

		public string ResourcesDir => Path.Combine(Folder, "Resources");

		public string TexturesDir => Path.Combine(Folder, "Textures");

		public string BundlesDir => Path.Combine(Folder, "Bundles");
	}

	// Finds the mods and indexes their files by the game's Resources path, e.g.
	// Mods/MyMod/Resources/Registry/Enemies.txt -> "registry/enemies" (lower case, no extension).
	// Only files in categories the player left on (in SvZ Mod Manager) are indexed, so everything
	// else in the loader honors those choices automatically.
	internal static class ModCatalog
	{
		public static readonly List<ModInfo> Mods = new List<ModInfo>();

		// Resources path -> files providing it, in load order (later ones win).
		private static readonly Dictionary<string, List<string>> s_Files = new Dictionary<string, List<string>>();

		// Texture name -> replacement image, for textures referenced directly by materials.
		public static readonly Dictionary<string, string> TextureOverrides = new Dictionary<string, string>();

		public static void Load(string modsRoot)
		{
			if (!Directory.Exists(modsRoot))
			{
				Directory.CreateDirectory(modsRoot);
			}
			var found = new List<ModInfo>();
			foreach (string dir in Directory.GetDirectories(modsRoot))
			{
				found.Add(new ModInfo(dir));
			}
			// Without the mod manager: higher priority loads later so its changes win; ties load alphabetically.
			found.Sort((a, b) => a.Manifest.Priority != b.Manifest.Priority ? a.Manifest.Priority.CompareTo(b.Manifest.Priority) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
			ModSelection selection = ModSelection.Read(modsRoot);
			if (selection != null)
			{
				// The manager's order; mods added since it last saved go after them.
				var ordered = new List<ModInfo>();
				foreach (ModSelection.Entry entry in selection.Entries)
				{
					ModInfo mod = found.Find(m => string.Equals(Path.GetFileName(m.Folder), entry.Folder, StringComparison.OrdinalIgnoreCase));
					if (mod != null)
					{
						mod.Choice = entry;
						ordered.Add(mod);
						found.Remove(mod);
					}
				}
				ordered.AddRange(found);
				found = ordered;
			}
			foreach (ModInfo mod in found)
			{
				if (!mod.Enabled)
				{
					Plugin.Log.LogInfo($"Mod '{mod.Name}' is turned off; skipping.");
					continue;
				}
				Mods.Add(mod);
				Index(mod);
				var off = new List<string>();
				foreach (ModCategory category in ModLayout.Categories)
				{
					if (!mod.Allows(category))
					{
						off.Add(ModLayout.Key(category));
					}
				}
				string version = mod.Manifest.Version.Length > 0 ? " " + mod.Manifest.Version : "";
				string author = mod.Manifest.Author.Length > 0 ? " by " + mod.Manifest.Author : "";
				string except = off.Count > 0 ? " (turned off: " + string.Join(", ", off.ToArray()) + ")" : "";
				Plugin.Log.LogInfo($"Loaded mod '{mod.Name}'{version}{author}{except}");
			}
			if (Mods.Count == 0)
			{
				Plugin.Log.LogInfo($"No mods found in {modsRoot}");
			}
		}

		private static bool Wanted(ModInfo mod, string file)
		{
			ModCategory? category = ModLayout.Classify(file.Substring(mod.Folder.Length + 1));
			return category.HasValue && mod.Allows(category.Value);
		}

		private static void Index(ModInfo mod)
		{
			if (Directory.Exists(mod.ResourcesDir))
			{
				foreach (string file in Directory.GetFiles(mod.ResourcesDir, "*", SearchOption.AllDirectories))
				{
					if (!Wanted(mod, file))
					{
						continue;
					}
					string key = KeyFor(file.Substring(mod.ResourcesDir.Length + 1));
					if (!s_Files.TryGetValue(key, out List<string> list))
					{
						s_Files[key] = list = new List<string>();
					}
					list.Add(file);
				}
			}
			if (Directory.Exists(mod.TexturesDir))
			{
				foreach (string file in Directory.GetFiles(mod.TexturesDir, "*", SearchOption.AllDirectories))
				{
					if (Wanted(mod, file))
					{
						TextureOverrides[Path.GetFileNameWithoutExtension(file).ToLowerInvariant()] = file;
					}
				}
			}
		}

		// "Registry\Enemies.txt" or "Registry/Enemies" -> "registry/enemies";
		// "Characters/PFBoss.variant.txt" -> "characters/pfboss.variant".
		public static string KeyFor(string path)
		{
			string p = path.Replace('\\', '/').Trim().ToLowerInvariant();
			int slash = p.LastIndexOf('/');
			int dot = p.LastIndexOf('.');
			if (dot > slash)
			{
				p = p.Substring(0, dot);
			}
			return p;
		}

		// All mod files for a Resources path whose extension is one of the given ones.
		public static IEnumerable<string> Files(string resourcesPath, params string[] extensions)
		{
			if (!s_Files.TryGetValue(KeyFor(resourcesPath + ".x"), out List<string> list))
			{
				yield break;
			}
			foreach (string file in list)
			{
				foreach (string ext in extensions)
				{
					if (file.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
					{
						yield return file;
						break;
					}
				}
			}
		}

		public static string Last(string resourcesPath, params string[] extensions)
		{
			string last = null;
			foreach (string file in Files(resourcesPath, extensions))
			{
				last = file;
			}
			return last;
		}
	}
}
