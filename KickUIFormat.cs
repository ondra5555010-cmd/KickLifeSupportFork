using System;

namespace KickLifeSupport
{
    internal static class KickUIFormat
    {
        public static string Good(string text) => Color(text, "#6fd36f");
        public static string Warning(string text) => Color(text, "#f0b44c");
        public static string Bad(string text) => Color(text, "#ff6666");

        public static string ReportLine(string text)
        {
            return "\n" + text;
        }

        public static string Timer(double seconds)
        {
            int totalSeconds = Math.Max(0, (int)Math.Ceiling(seconds));
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int secs = totalSeconds % 60;
            return $"{hours}:{minutes:00}:{secs:00}";
        }

        static string Color(string text, string color)
        {
            return $"<color={color}>{text}</color>";
        }
    }
}
