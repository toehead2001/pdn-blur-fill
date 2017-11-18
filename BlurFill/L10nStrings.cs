using System.Globalization;

namespace BlurFillEffect
{
    internal static class L10nStrings
    {
        private static readonly string UICulture = CultureInfo.CurrentUICulture.Name;

        internal static string EffectName
        {
            get
            {
                switch (UICulture)
                {
                    case "ru":
                        return "Заливка размытием";
                    default:
                        return "Blur Fill";
                }
            }
        }

        internal static string EffectMenu
        {
            get
            {
                switch (UICulture)
                {
                    default:
                        return "Fill";
                }
            }
        }

        internal static string EffectDescription
        {
            get
            {
                switch (UICulture)
                {
                    case "ru":
                        return string.Empty;
                    default:
                        return "Fill the transparent areas of the canvas";
                }
            }
        }

        internal static string EffectKeywords
        {
            get
            {
                switch (UICulture)
                {
                    default:
                        return string.Empty;
                }
            }
        }

        internal static string BlurRadius
        {
            get
            {
                switch (UICulture)
                {
                    case "ru":
                        return "Радиус размытия";
                    default:
                        return "Blur Radius";
                }
            }
        }

        internal static string Brightness
        {
            get
            {
                switch (UICulture)
                {
                    case "ru":
                        return "Яркость";
                    default:
                        return "Brightness";
                }
            }
        }

        internal static string Position
        {
            get
            {
                switch (UICulture)
                {
                    case "ru":
                        return "Настройка позиции";
                    default:
                        return "Position Adjust";
                }
            }
        }

        internal static string KeepOriginal
        {
            get
            {
                switch (UICulture)
                {
                    case "ru":
                        return "Сохранять исходное изображение";
                    default:
                        return "Keep original image";
                }
            }
        }
    }
}
