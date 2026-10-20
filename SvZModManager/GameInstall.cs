using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SvZModManager
{
	internal enum LoaderState
	{
		NotInstalled,
		Outdated,
		Installed,
	}

	// A copy of the game on disk, and the mod loader in it: BepInEx (started by Doorstop's
	// version.dll) plus the SvZ mod loader plugin. Installing adds files; no game file is changed.
	internal class GameInstall
	{
		private const string PluginPath = @"BepInEx\plugins\SvZModLoader\SvZModLoader.dll";
		private const string DoorstopConfig = "doorstop_config.ini";

		public readonly string Folder;
		public readonly string Exe;

		private GameInstall(string folder, string exe)
		{
			Folder = folder;
			Exe = exe;
		}

		public string ModsFolder => Path.Combine(Folder, "Mods");

		// The folder holds a Unity game: an exe with a matching <name>_Data\Managed\Assembly-CSharp.dll.
		public static GameInstall TryOpen(string folder)
		{
			if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
			{
				return null;
			}
			foreach (string exe in Directory.GetFiles(folder, "*.exe"))
			{
				string data = Path.Combine(folder, Path.GetFileNameWithoutExtension(exe) + "_Data");
				if (File.Exists(Path.Combine(data, "Managed", "Assembly-CSharp.dll")))
				{
					return new GameInstall(folder, exe);
				}
			}
			return null;
		}

		private static byte[] Resource(string name)
		{
			using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
			using var memory = new MemoryStream();
			stream.CopyTo(memory);
			return memory.ToArray();
		}

		public LoaderState Loader
		{
			get
			{
				string plugin = Path.Combine(Folder, PluginPath);
				bool proxy = File.Exists(Path.Combine(Folder, "version.dll")) || File.Exists(Path.Combine(Folder, "winhttp.dll"));
				if (!proxy || !File.Exists(Path.Combine(Folder, @"BepInEx\core\BepInEx.Preloader.dll")) || !File.Exists(plugin))
				{
					return LoaderState.NotInstalled;
				}
				return File.ReadAllBytes(plugin).SequenceEqual(Resource("SvZModLoader.dll")) ? LoaderState.Installed : LoaderState.Outdated;
			}
		}

		public void InstallLoader()
		{
			using (var zip = new ZipArchive(new MemoryStream(Resource("BepInEx.zip"))))
			{
				foreach (ZipArchiveEntry entry in zip.Entries)
				{
					if (entry.FullName.EndsWith("/"))
					{
						continue;
					}
					// This game loads winhttp.dll too late for Doorstop; as version.dll it starts in time.
					string name = entry.FullName == "winhttp.dll" ? "version.dll" : entry.FullName;
					if (name == DoorstopConfig && File.Exists(Path.Combine(Folder, name)))
					{
						continue;
					}
					string target = Path.Combine(Folder, name.Replace('/', Path.DirectorySeparatorChar));
					Directory.CreateDirectory(Path.GetDirectoryName(target));
					entry.ExtractToFile(target, true);
				}
			}
			string plugin = Path.Combine(Folder, PluginPath);
			Directory.CreateDirectory(Path.GetDirectoryName(plugin));
			File.WriteAllBytes(plugin, Resource("SvZModLoader.dll"));
			Directory.CreateDirectory(ModsFolder);
			ModsEnabled = true;
		}

		// Removes everything InstallLoader added except the Mods folder.
		public void UninstallLoader()
		{
			using (var zip = new ZipArchive(new MemoryStream(Resource("BepInEx.zip"))))
			{
				foreach (ZipArchiveEntry entry in zip.Entries.Where(e => !e.FullName.Contains('/')))
				{
					foreach (string name in entry.FullName == "winhttp.dll" ? new[] { "version.dll", "winhttp.dll" } : new[] { entry.FullName })
					{
						string file = Path.Combine(Folder, name);
						if (File.Exists(file))
						{
							File.Delete(file);
						}
					}
				}
			}
			string bepinex = Path.Combine(Folder, "BepInEx");
			if (Directory.Exists(bepinex))
			{
				Directory.Delete(bepinex, true);
			}
		}

		// Doorstop's own on/off switch: off starts the game exactly as shipped, without any mods.
		public bool ModsEnabled
		{
			get
			{
				string file = Path.Combine(Folder, DoorstopConfig);
				return !File.Exists(file) || !Regex.IsMatch(File.ReadAllText(file), @"^\s*enabled\s*=\s*false", RegexOptions.Multiline | RegexOptions.IgnoreCase);
			}
			set
			{
				string file = Path.Combine(Folder, DoorstopConfig);
				if (File.Exists(file))
				{
					string text = Regex.Replace(File.ReadAllText(file), @"^(\s*enabled\s*=\s*)\w+", "${1}" + (value ? "true" : "false"), RegexOptions.Multiline | RegexOptions.IgnoreCase);
					File.WriteAllText(file, text);
				}
			}
		}
	}
}
