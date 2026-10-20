using UnityEngine;

namespace SvZModLoader
{
	// A laser shot drawn from a point to a target: a bright core and a wider glow (two line renderers)
	// plus a flash where it hits, flickering and fading out over a fraction of a second. Purely visual;
	// the game's own (instant) bullet hit does the damage.
	internal class LaserBeam : MonoBehaviour
	{
		internal class Settings
		{
			public string From = "";
			public Color Color = new Color(1f, 0.15f, 0.1f);
			public float Width = 6f;
			public float Duration = 0.3f;
		}

		private static Material s_Material;
		private static Texture2D s_BeamTexture;
		private static Texture2D s_FlashTexture;

		private Settings m_Settings;
		private LineRenderer m_Core;
		private LineRenderer m_Glow;
		private Transform m_Flash;
		private float m_Age;

		public static void Fire(Vector3 from, Vector3 to, Settings settings)
		{
			var beam = new GameObject("SvZModLoader Laser").AddComponent<LaserBeam>();
			beam.m_Settings = settings;
			beam.m_Glow = beam.AddLine("Glow", from, to, Shared(), settings.Width * 3f);
			beam.m_Core = beam.AddLine("Core", from, to, Shared(), settings.Width);
			beam.m_Flash = beam.AddFlash(to, settings.Width * 6f);
			beam.Refresh(1f);
		}

		private LineRenderer AddLine(string name, Vector3 from, Vector3 to, Material material, float width)
		{
			var line = new GameObject(name).AddComponent<LineRenderer>();
			line.transform.SetParent(transform, false);
			line.useWorldSpace = true;
			line.positionCount = 2;
			line.SetPosition(0, from);
			line.SetPosition(1, to);
			line.startWidth = width;
			line.endWidth = width;
			line.numCapVertices = 4;
			line.textureMode = LineTextureMode.Stretch;
			line.sharedMaterial = material;
			line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			line.receiveShadows = false;
			return line;
		}

		private Transform AddFlash(Vector3 at, float size)
		{
			var flash = new GameObject("Flash");
			flash.transform.SetParent(transform, false);
			flash.transform.position = at;
			flash.transform.localScale = Vector3.one * size;
			var quad = new Mesh
			{
				vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) },
				uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) },
				triangles = new[] { 0, 2, 1, 2, 3, 1 },
			};
			flash.AddComponent<MeshFilter>().sharedMesh = quad;
			flash.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shared()) { mainTexture = s_FlashTexture };
			return flash.transform;
		}

		private void Update()
		{
			m_Age += Time.deltaTime;
			float life = 1f - m_Age / Mathf.Max(0.01f, m_Settings.Duration);
			if (life <= 0f)
			{
				Destroy(m_Flash.GetComponent<Renderer>().sharedMaterial);
				Destroy(m_Flash.GetComponent<MeshFilter>().sharedMesh);
				Destroy(gameObject);
				return;
			}
			Refresh(life);
		}

		private void Refresh(float life)
		{
			float flicker = 0.85f + 0.15f * Mathf.Sin(m_Age * 90f);
			Color glow = m_Settings.Color * (life * flicker);
			Color core = Color.Lerp(m_Settings.Color, Color.white, 0.6f) * life;
			m_Glow.startColor = m_Glow.endColor = glow;
			m_Core.startColor = m_Core.endColor = core;
			m_Core.startWidth = m_Core.endWidth = m_Settings.Width * (0.6f + 0.4f * life) * flicker;
			Material flash = m_Flash.GetComponent<Renderer>().sharedMaterial;
			flash.SetColor(flash.HasProperty("_TintColor") ? "_TintColor" : "_Color", m_Settings.Color * (0.5f * life));
			if (Camera.main != null)
			{
				m_Flash.rotation = Camera.main.transform.rotation;
			}
			m_Flash.localScale = Vector3.one * m_Settings.Width * (6f + 4f * (1f - life));
		}

		// One additive material for every beam, with a soft-edged strip texture.
		private static Material Shared()
		{
			if (s_Material != null)
			{
				return s_Material;
			}
			s_BeamTexture = GradientTexture(4, 64, (x, y) => Mathf.Exp(-Mathf.Pow((y - 0.5f) / 0.2f, 2f)));
			s_FlashTexture = GradientTexture(32, 32, (x, y) => Mathf.Exp(-((x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f)) / 0.03f));
			Shader shader = null;
			foreach (string name in new[] { "Legacy Shaders/Particles/Additive", "Particles/Additive", "Mobile/Particles/Additive", "Sprites/Default" })
			{
				shader = Shader.Find(name);
				if (shader != null)
				{
					Plugin.Log.LogInfo($"Lasers use shader '{name}'");
					break;
				}
			}
			s_Material = new Material(shader) { mainTexture = s_BeamTexture };
			if (s_Material.HasProperty("_TintColor"))
			{
				s_Material.SetColor("_TintColor", new Color(0.5f, 0.5f, 0.5f, 0.5f));
			}
			return s_Material;
		}

		private static Texture2D GradientTexture(int width, int height, System.Func<float, float, float> brightness)
		{
			var texture = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					float b = brightness((x + 0.5f) / width, (y + 0.5f) / height);
					texture.SetPixel(x, y, new Color(b, b, b, b));
				}
			}
			texture.Apply();
			return texture;
		}
	}
}
