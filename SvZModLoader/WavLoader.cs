using System;
using System.IO;
using UnityEngine;

namespace SvZModLoader
{
	// Minimal WAV reader (PCM 8/16/24/32-bit or 32-bit float) so sounds can be dropped in as loose
	// files without building an asset bundle.
	internal static class WavLoader
	{
		public static AudioClip Load(string file)
		{
			byte[] data = File.ReadAllBytes(file);
			if (data.Length < 12 || Tag(data, 0) != "RIFF" || Tag(data, 8) != "WAVE")
			{
				Plugin.Log.LogError($"{file}: not a WAV file");
				return null;
			}
			int format = 0, channels = 0, rate = 0, bits = 0, dataStart = -1, dataLength = 0;
			for (int pos = 12; pos + 8 <= data.Length;)
			{
				string id = Tag(data, pos);
				int size = BitConverter.ToInt32(data, pos + 4);
				if (id == "fmt ")
				{
					format = BitConverter.ToUInt16(data, pos + 8);
					channels = BitConverter.ToUInt16(data, pos + 10);
					rate = BitConverter.ToInt32(data, pos + 12);
					bits = BitConverter.ToUInt16(data, pos + 22);
				}
				else if (id == "data")
				{
					dataStart = pos + 8;
					dataLength = Math.Min(size, data.Length - dataStart);
				}
				pos += 8 + size + (size & 1);
			}
			if (dataStart < 0 || channels == 0 || (format != 1 && format != 3 && format != 0xFFFE))
			{
				Plugin.Log.LogError($"{file}: unsupported WAV layout (format {format})");
				return null;
			}
			int bytesPerSample = bits / 8;
			int count = dataLength / bytesPerSample;
			var samples = new float[count];
			for (int i = 0; i < count; i++)
			{
				int at = dataStart + i * bytesPerSample;
				switch (bits)
				{
				case 8: samples[i] = (data[at] - 128) / 128f; break;
				case 16: samples[i] = BitConverter.ToInt16(data, at) / 32768f; break;
				case 24: samples[i] = ((data[at] | (data[at + 1] << 8) | ((sbyte)data[at + 2] << 16))) / 8388608f; break;
				case 32: samples[i] = format == 3 ? BitConverter.ToSingle(data, at) : BitConverter.ToInt32(data, at) / 2147483648f; break;
				}
			}
			AudioClip clip = AudioClip.Create(Path.GetFileNameWithoutExtension(file), count / channels, channels, rate, false);
			clip.SetData(samples, 0);
			return clip;
		}

		private static string Tag(byte[] data, int at)
		{
			return System.Text.Encoding.ASCII.GetString(data, at, 4);
		}
	}
}
