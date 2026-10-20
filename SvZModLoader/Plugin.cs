using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SvZModLoader
{
	// Entry point. Finds the mods under <game>/Mods, then hooks the game so that:
	//  - registry/text data (SDF files) gets each mod's changes merged in (RegistryPatches),
	//  - every Resources.Load in the game goes through ModAssets first (ResourcesRedirect), which
	//    serves asset bundles, prefab variants and loose PNG/WAV files from mods.
	[BepInPlugin(Guid, "SvZ Mod Loader", "1.0.0")]
	public class Plugin : BaseUnityPlugin
	{
		public const string Guid = "svz.modloader";

		internal static ManualLogSource Log;
		internal static ConfigEntry<bool> DumpData;
		internal static ConfigEntry<bool> LogOverrides;
		internal static ConfigEntry<float> AutoScreenshotSeconds;
		internal static string DumpFolder;

		private float m_NextScreenshot;
		private int m_ScreenshotIndex;

		private void Awake()
		{
			Log = Logger;
			string modsFolder = Config.Bind("General", "ModsFolder", "Mods",
				"Folder (relative to the game folder) that holds one sub-folder per mod.").Value;
			DumpData = Config.Bind("Modding", "DumpOriginalData", false,
				"Write every registry/text file the game loads, as shipped, to BepInEx/dump/ (useful as a starting point for mods).");
			LogOverrides = Config.Bind("Modding", "LogOverrides", true,
				"Log each registry merge and asset the mods replace.");
			AutoScreenshotSeconds = Config.Bind("Debug", "AutoScreenshotSeconds", 0f,
				"If above zero, save a screenshot to BepInEx/screenshots every this many seconds.");
			if (Config.Bind("Debug", "RunInBackground", false,
				"Keep the game running while its window isn't focused (for testing mods unattended).").Value)
			{
				Application.runInBackground = true;
			}
			DumpFolder = Path.Combine(Paths.BepInExRootPath, "dump");

			ModCatalog.Load(Path.Combine(Paths.GameRootPath, modsFolder));
			ModAssets.Init();
			var harmony = new Harmony(Guid);
			harmony.PatchAll(typeof(RegistryPatches));
			harmony.PatchAll(typeof(ProjectilePatches));
			int sites = ResourcesRedirect.Install(harmony);
			Log.LogInfo($"Redirected {sites} Resources.Load call sites in the game code through the mod loader.");
			UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) => ModAssets.OnSceneLoaded(scene);
		}

		private void Update()
		{
			if (AutoScreenshotSeconds.Value > 0f && Time.realtimeSinceStartup >= m_NextScreenshot)
			{
				m_NextScreenshot = Time.realtimeSinceStartup + AutoScreenshotSeconds.Value;
				string folder = Path.Combine(Paths.BepInExRootPath, "screenshots");
				Directory.CreateDirectory(folder);
				ScreenCapture.CaptureScreenshot(Path.Combine(folder, $"{m_ScreenshotIndex++:000}.png"));
			}
		}
	}
}
