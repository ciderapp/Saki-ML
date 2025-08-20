using System;

namespace Saki_ML.Utils
{
    public static class TextUtils
    {
        public static string Truncate(string text, int max = 160)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }
    }
}


