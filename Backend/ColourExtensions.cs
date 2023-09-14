using System;
using System.Drawing;

namespace Mission.TimeTable.Domain.Utility
{
    public static class ColourExtensions
    {
        public static Color ToColor(this string cssColor)
        {
            if (string.IsNullOrEmpty(cssColor))
            {
                throw new NullReferenceException(nameof(cssColor));
            }

            cssColor = cssColor.Trim();

            if (cssColor.StartsWith("#"))
            {
                return ColorTranslator.FromHtml(cssColor);
            }
            else if (System.Enum.TryParse(cssColor, out KnownColor _))
            {
                return Color.FromName(cssColor);
            }
            else
            {
                var hexCode = "#" + cssColor;
                return ColorTranslator.FromHtml(hexCode);
            }
        }

        public static string GetHexCode(this Color value)
        {            
            if (value.A == 0 && value.R == 0 && value.G == 0 && value.B == 0 && !string.IsNullOrEmpty(value.Name) && value.Name.Length <= 6)
            {
                value = ColorTranslator.FromHtml("#" + value.Name);
            }

            return "#" + value.R.ToString("X2") + value.G.ToString("X2") + value.B.ToString("X2");
        }
    }
}
