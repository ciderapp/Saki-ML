using System;

namespace Saki_ML.Utils
{
	public static class Ansi
	{
		public static string Colorize(string text, string hexColor)
		{
			if (!ShouldColorize() || string.IsNullOrWhiteSpace(hexColor)) return text;
			if (!TryParseHex(hexColor, out var r, out var g, out var b)) return text;
			return $"\u001b[38;2;{r};{g};{b}m{text}\u001b[0m";
		}

		private static bool ShouldColorize()
		{
			var env = Environment.GetEnvironmentVariable("SAKI_ML_LOG_COLOR");
			if (string.Equals(env, "false", StringComparison.OrdinalIgnoreCase)) return false;
			return !Console.IsOutputRedirected; // default to color when interactive console
		}

		private static bool TryParseHex(string hex, out int r, out int g, out int b)
		{
			r = g = b = 0;
			hex = hex.StartsWith("#") ? hex.Substring(1) : hex;
			if (hex.Length != 6) return false;
			try
			{
				r = Convert.ToInt32(hex.Substring(0, 2), 16);
				g = Convert.ToInt32(hex.Substring(2, 2), 16);
				b = Convert.ToInt32(hex.Substring(4, 2), 16);
				return true;
			}
			catch
			{
				return false;
			}
		}
	}
}


