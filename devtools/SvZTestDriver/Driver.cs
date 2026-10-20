using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace SvZTestDriver
{
	// TEST ONLY - for checking mods unattended; not part of the mod loader or what players install.
	// Runs the commands in BepInEx/driver.txt (written by the test script), one per line:
	//   tap X Y     tap the screen at pixel X, Y (from the top-left), as a mouse click would
	//   wait S      wait S seconds
	//   shot NAME   save a screenshot to BepInEx/shots/NAME.png
	//   scene NAME  wait (up to a minute) until scene NAME is loaded, plus a second to settle
	//   load NAME   load scene NAME (e.g. skip the intro movie the way its own skip does)
	//   profile     log the loaded save's wave and hero/sword/bow levels
	[BepInPlugin("svz.testdriver", "SvZ Test Driver", "1.0.0")]
	public class Driver : BaseUnityPlugin
	{
		private static readonly FieldInfo s_FakeTouches = AccessTools.Field(typeof(PCInput), "fakeTouches");
		private static readonly Queue<string> s_Commands = new Queue<string>();
		private static Vector2 s_TapPosition;
		private static int s_TapStage;
		private static float s_WaitUntil;
		private static string s_WaitScene;
		private static string s_LastScene = "";

		private string m_CommandFile;

		private void Awake()
		{
			m_CommandFile = Path.Combine(Paths.BepInExRootPath, "driver.txt");
			UnityEngine.SceneManagement.SceneManager.sceneLoaded += (scene, mode) =>
			{
				s_LastScene = scene.name;
				Logger.LogInfo("driver: scene loaded: " + scene.name);
			};
			new Harmony("svz.testdriver").Patch(AccessTools.Method(typeof(PCInput), "Update"), postfix: new HarmonyMethod(typeof(Driver), nameof(AfterInput)));
		}

		private float m_NextHeartbeat;

		private void Update()
		{
			if (Time.realtimeSinceStartup >= m_NextHeartbeat)
			{
				m_NextHeartbeat = Time.realtimeSinceStartup + 5f;
				Logger.LogInfo($"driver: heartbeat t={Time.realtimeSinceStartup:0.0} frame={Time.frameCount} timeScale={Time.timeScale} focus={Application.isFocused} queued={s_Commands.Count}");
			}
			if (File.Exists(m_CommandFile))
			{
				foreach (string line in File.ReadAllLines(m_CommandFile))
				{
					if (line.Trim().Length > 0)
					{
						s_Commands.Enqueue(line.Trim());
					}
				}
				File.Delete(m_CommandFile);
			}
			if (s_WaitScene != null && (s_LastScene.ToLowerInvariant() == s_WaitScene || Time.realtimeSinceStartup >= s_WaitUntil))
			{
				Logger.LogInfo("driver: " + (s_LastScene.ToLowerInvariant() == s_WaitScene ? "reached " : "timed out waiting for ") + s_WaitScene);
				s_WaitScene = null;
				s_WaitUntil = Time.realtimeSinceStartup + 1f;
			}
			while (s_Commands.Count > 0 && s_TapStage == 0 && s_WaitScene == null && Time.realtimeSinceStartup >= s_WaitUntil)
			{
				string[] command = s_Commands.Dequeue().Split(' ');
				Logger.LogInfo("driver: " + string.Join(" ", command));
				switch (command[0])
				{
				case "tap":
					s_TapPosition = new Vector2(Number(command[1]), Screen.height - Number(command[2]));
					s_TapStage = 1;
					break;
				case "wait":
					s_WaitUntil = Time.realtimeSinceStartup + Number(command[1]);
					break;
				case "scene":
					s_WaitScene = command[1].ToLowerInvariant();
					s_WaitUntil = Time.realtimeSinceStartup + 60f;
					break;
				case "profile":
					Profile profile = Singleton<Profile>.instance;
					Logger.LogInfo($"driver: profile waveToBeat={profile.waveToBeat} heroLevel={profile.heroLevel} swordLevel={profile.swordLevel} bowLevel={profile.bowLevel} highestUnlockedWave={profile.highestUnlockedWave}");
					break;
				case "load":
					Application.LoadLevel(command[1]);
					break;
				case "shot":
					string folder = Path.Combine(Paths.BepInExRootPath, "shots");
					Directory.CreateDirectory(folder);
					ScreenCapture.CaptureScreenshot(Path.Combine(folder, command[1] + ".png"));
					break;
				}
			}
		}

		private static float Number(string text)
		{
			return float.Parse(text, CultureInfo.InvariantCulture);
		}

		// After the game turns the mouse into touches, replace them with the scripted tap:
		// pressed on one frame, released on the next.
		private static void AfterInput()
		{
			if (s_TapStage == 0)
			{
				return;
			}
			TouchPhase phase = s_TapStage == 1 ? TouchPhase.Began : TouchPhase.Ended;
			s_FakeTouches.SetValue(null, new[] { new Touch { position = s_TapPosition, phase = phase } });
			s_TapStage = s_TapStage == 1 ? 2 : 0;
		}
	}
}
