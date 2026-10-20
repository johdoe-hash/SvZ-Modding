using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SvZModLoader
{
	// Just enough JSON for glTF: objects -> Dictionary<string, object>, arrays -> List<object>,
	// numbers -> double, plus string, bool and null.
	internal static class MiniJson
	{
		public static object Parse(string json)
		{
			int i = 0;
			object value = ReadValue(json, ref i);
			return value;
		}

		private static void SkipSpace(string s, ref int i)
		{
			while (i < s.Length && char.IsWhiteSpace(s[i]))
			{
				i++;
			}
		}

		private static object ReadValue(string s, ref int i)
		{
			SkipSpace(s, ref i);
			char c = s[i];
			if (c == '{')
			{
				var obj = new Dictionary<string, object>();
				i++;
				SkipSpace(s, ref i);
				if (s[i] == '}')
				{
					i++;
					return obj;
				}
				while (true)
				{
					SkipSpace(s, ref i);
					string key = ReadString(s, ref i);
					SkipSpace(s, ref i);
					i++; // ':'
					obj[key] = ReadValue(s, ref i);
					SkipSpace(s, ref i);
					if (s[i++] == '}')
					{
						return obj;
					}
				}
			}
			if (c == '[')
			{
				var list = new List<object>();
				i++;
				SkipSpace(s, ref i);
				if (s[i] == ']')
				{
					i++;
					return list;
				}
				while (true)
				{
					list.Add(ReadValue(s, ref i));
					SkipSpace(s, ref i);
					if (s[i++] == ']')
					{
						return list;
					}
				}
			}
			if (c == '"')
			{
				return ReadString(s, ref i);
			}
			if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "true", 0, 4) == 0)
			{
				i += 4;
				return true;
			}
			if (s.Length - i >= 5 && string.CompareOrdinal(s, i, "false", 0, 5) == 0)
			{
				i += 5;
				return false;
			}
			if (s.Length - i >= 4 && string.CompareOrdinal(s, i, "null", 0, 4) == 0)
			{
				i += 4;
				return null;
			}
			int start = i;
			while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0)
			{
				i++;
			}
			return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
		}

		private static string ReadString(string s, ref int i)
		{
			var text = new StringBuilder();
			i++; // opening quote
			while (s[i] != '"')
			{
				char c = s[i++];
				if (c != '\\')
				{
					text.Append(c);
					continue;
				}
				char e = s[i++];
				switch (e)
				{
				case 'n': text.Append('\n'); break;
				case 't': text.Append('\t'); break;
				case 'r': text.Append('\r'); break;
				case 'b': text.Append('\b'); break;
				case 'f': text.Append('\f'); break;
				case 'u': text.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
				default: text.Append(e); break;
				}
			}
			i++; // closing quote
			return text.ToString();
		}
	}
}
