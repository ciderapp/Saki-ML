using Saki_ML.Contracts;

namespace Saki_ML.Utils
{
	public static class LogStyle
	{
		public static string Glyph(ClassificationVerdict verdict)
		{
			return verdict switch
			{
				ClassificationVerdict.Allow => "🟢",
				ClassificationVerdict.Unsure => "🟡",
				ClassificationVerdict.Block => "🔴",
				_ => "" 
			};
		}
	}
}


