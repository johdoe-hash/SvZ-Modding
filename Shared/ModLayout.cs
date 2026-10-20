using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Shared by the in-game loader (SvZModLoader, .NET 3.5) and SvZ Mod Manager (.NET 8), so both read
// mods and the player's choices the same way. Keep it .NET 3.5-compatible.
namespace SvZModding
{
	// The kinds of change a mod can make. The mod manager lets players switch each one off per mod.
	public enum ModCategory
	{
		Data,
		Text,
		Textures,
		Sounds,
		Models,
	}

	public static class ModLayout
	{
		// Optional description file inside each mod folder.
		public const string InfoFile = "mod.txt";

		// The mod manager's choices, inside the Mods folder.
		public const string SelectionFile = "modlist.txt";

		public static readonly ModCategory[] Categories =
		{
			ModCategory.Data, ModCategory.Text, ModCategory.Textures, ModCategory.Sounds, ModCategory.Models,
		};

		public static string Key(ModCategory category)
		{
			return category.ToString().ToLowerInvariant();
		}

		public static string Describe(ModCategory category)
		{
			switch (category)
			{
			case ModCategory.Data: return "Game data (stats, waves, enemies, store)";
			case ModCategory.Text: return "Text";
			case ModCategory.Textures: return "Textures and images";
			case ModCategory.Sounds: return "Sounds";
			default: return "Models, animations and new characters";
			}
		}

		// Which category a file in a mod belongs to (path relative to the mod folder), or null if
		// the loader doesn't use it. A mod folder can contain:
		//   Resources/<game path>.txt          data merged into the game's data file (Text/... is text)
		//   Resources/<game path>.variant.txt  a new model made from an existing one (plus any .glb it uses)
		//   Resources/<game path>.png|.jpg     a replacement image
		//   Resources/<game path>.wav          a replacement sound
		//   Textures/<texture name>.png|.jpg   replaces that texture wherever the game uses it
		//   Bundles/<file>                     Unity 2017.4 asset bundle with new models/animations
		public static ModCategory? Classify(string relativePath)
		{
			string p = relativePath.Replace('\\', '/').ToLowerInvariant();
			if (p.StartsWith("resources/"))
			{
				p = p.Substring("resources/".Length);
				if (p.EndsWith(".variant.txt") || p.EndsWith(".glb") || p.EndsWith(".gltf"))
				{
					return ModCategory.Models;
				}
				if (p.EndsWith(".txt"))
				{
					return p.StartsWith("text/") ? ModCategory.Text : ModCategory.Data;
				}
				if (IsImage(p))
				{
					return ModCategory.Textures;
				}
				if (p.EndsWith(".wav"))
				{
					return ModCategory.Sounds;
				}
				return null;
			}
			if (p.StartsWith("textures/") && IsImage(p))
			{
				return ModCategory.Textures;
			}
			if (p.StartsWith("bundles/") && !p.EndsWith(".manifest"))
			{
				return ModCategory.Models;
			}
			return null;
		}

		public static bool IsImage(string path)
		{
			return path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
		}

		public static bool ParseBool(string value, bool fallback)
		{
			switch (value.Trim().ToLowerInvariant())
			{
			case "yes": case "true": case "on": case "1": return true;
			case "no": case "false": case "off": case "0": return false;
			default: return fallback;
			}
		}

		// "key = value" lines; '#' and '/' start comments. Calls back with [section] names too.
		internal static void ReadLines(string file, Action<string> onSection, Action<string, string> onValue)
		{
			foreach (string raw in File.ReadAllLines(file))
			{
				string line = raw.Trim();
				if (line.Length == 0 || line[0] == '#' || line[0] == '/')
				{
					continue;
				}
				if (line[0] == '[' && line[line.Length - 1] == ']')
				{
					if (onSection != null)
					{
						onSection(line.Substring(1, line.Length - 2).Trim());
					}
					continue;
				}
				int eq = line.IndexOf('=');
				if (eq > 0)
				{
					onValue(line.Substring(0, eq).Trim().ToLowerInvariant(), line.Substring(eq + 1).Trim());
				}
			}
		}
	}

	// mod.txt: name, version, author, description, priority (higher loads later and wins) and
	// enabled (the default when the mod manager hasn't been used).
	public class ModManifest
	{
		public string Name = "";
		public string Version = "";
		public string Author = "";
		public string Description = "";
		public int Priority;
		public bool Enabled = true;

		public static ModManifest Read(string modFolder)
		{
			var manifest = new ModManifest { Name = Path.GetFileName(modFolder) };
			string file = Path.Combine(modFolder, ModLayout.InfoFile);
			if (!File.Exists(file))
			{
				return manifest;
			}
			ModLayout.ReadLines(file, null, (key, value) =>
			{
				switch (key)
				{
				case "name": manifest.Name = value; break;
				case "version": manifest.Version = value; break;
				case "author": manifest.Author = value; break;
				case "description": manifest.Description = value.Replace("\\n", "\n"); break;
				case "priority": int.TryParse(value, out manifest.Priority); break;
				case "enabled": manifest.Enabled = ModLayout.ParseBool(value, true); break;
				}
			});
			return manifest;
		}
	}

	// The player's choices from the mod manager: load order (top to bottom; later mods win where
	// they overlap), which mods are on, and which kinds of change each mod may make.
	public class ModSelection
	{
		public class Entry
		{
			public string Folder;
			public bool Enabled = true;
			public readonly HashSet<ModCategory> Off = new HashSet<ModCategory>();

			public bool Allows(ModCategory category)
			{
				return Enabled && !Off.Contains(category);
			}
		}

		public readonly List<Entry> Entries = new List<Entry>();

		public Entry Find(string folder)
		{
			foreach (Entry entry in Entries)
			{
				if (string.Equals(entry.Folder, folder, StringComparison.OrdinalIgnoreCase))
				{
					return entry;
				}
			}
			return null;
		}

		// Null when the mod manager hasn't saved a selection yet.
		public static ModSelection Read(string modsRoot)
		{
			string file = Path.Combine(modsRoot, ModLayout.SelectionFile);
			if (!File.Exists(file))
			{
				return null;
			}
			var selection = new ModSelection();
			Entry current = null;
			ModLayout.ReadLines(file, section =>
			{
				current = new Entry { Folder = section };
				selection.Entries.Add(current);
			}, (key, value) =>
			{
				if (current == null)
				{
					return;
				}
				if (key == "enabled")
				{
					current.Enabled = ModLayout.ParseBool(value, true);
					return;
				}
				foreach (ModCategory category in ModLayout.Categories)
				{
					if (key == ModLayout.Key(category) && !ModLayout.ParseBool(value, true))
					{
						current.Off.Add(category);
					}
				}
			});
			return selection;
		}

		public void Write(string modsRoot)
		{
			var text = new StringBuilder();
			text.AppendLine("# Saved by SvZ Mod Manager. Mods load from top to bottom; where two mods change the same");
			text.AppendLine("# thing, the lower one wins. Delete this file to go back to each mod's own mod.txt settings.");
			foreach (Entry entry in Entries)
			{
				text.AppendLine();
				text.AppendLine("[" + entry.Folder + "]");
				text.AppendLine("enabled = " + (entry.Enabled ? "yes" : "no"));
				foreach (ModCategory category in ModLayout.Categories)
				{
					text.AppendLine(ModLayout.Key(category) + " = " + (entry.Off.Contains(category) ? "no" : "yes"));
				}
			}
			File.WriteAllText(Path.Combine(modsRoot, ModLayout.SelectionFile), text.ToString());
		}
	}
}
