using System;
using System.Windows.Media;

namespace TriggerPoint.UI.Theme;

public static class ColorMath
{
    public static (double H, double S, double L) RgbToHsl(byte r, byte g, byte b)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;

        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double l = (max + min) / 2.0;
        double h = 0.0;
        double s = 0.0;

        if (delta > 0.00001)
        {
            s = l <= 0.5 ? (delta / (max + min)) : (delta / (2.0 - max - min));

            if (Math.Abs(max - rd) < 0.00001)
            {
                h = ((gd - bd) / delta) + (gd < bd ? 6.0 : 0.0);
            }
            else if (Math.Abs(max - gd) < 0.00001)
            {
                h = ((bd - rd) / delta) + 2.0;
            }
            else
            {
                h = ((rd - gd) / delta) + 4.0;
            }

            h *= 60.0;
        }

        return (Math.Clamp(h, 0.0, 360.0), Math.Clamp(s, 0.0, 1.0), Math.Clamp(l, 0.0, 1.0));
    }

    public static (double H, double S, double L) RgbToHsl(Color color) =>
        RgbToHsl(color.R, color.G, color.B);

    public static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
    {
        h = h % 360.0;
        if (h < 0.0) h += 360.0;
        s = Math.Clamp(s, 0.0, 1.0);
        l = Math.Clamp(l, 0.0, 1.0);

        if (s <= 0.00001)
        {
            byte gray = (byte)Math.Round(l * 255.0);
            return (gray, gray, gray);
        }

        double c = (1.0 - Math.Abs((2.0 * l) - 1.0)) * s;
        double x = c * (1.0 - Math.Abs(((h / 60.0) % 2.0) - 1.0));
        double m = l - (c / 2.0);

        double rPrime = 0.0, gPrime = 0.0, bPrime = 0.0;

        if (h < 60.0)
        {
            rPrime = c; gPrime = x; bPrime = 0.0;
        }
        else if (h < 120.0)
        {
            rPrime = x; gPrime = c; bPrime = 0.0;
        }
        else if (h < 180.0)
        {
            rPrime = 0.0; gPrime = c; bPrime = x;
        }
        else if (h < 240.0)
        {
            rPrime = 0.0; gPrime = x; bPrime = c;
        }
        else if (h < 300.0)
        {
            rPrime = x; gPrime = 0.0; bPrime = c;
        }
        else
        {
            rPrime = c; gPrime = 0.0; bPrime = x;
        }

        byte r = (byte)Math.Clamp(Math.Round((rPrime + m) * 255.0), 0.0, 255.0);
        byte g = (byte)Math.Clamp(Math.Round((gPrime + m) * 255.0), 0.0, 255.0);
        byte b = (byte)Math.Clamp(Math.Round((bPrime + m) * 255.0), 0.0, 255.0);

        return (r, g, b);
    }

    public static Color HslToColor(double h, double s, double l)
    {
        var (r, g, b) = HslToRgb(h, s, l);
        return Color.FromRgb(r, g, b);
    }

    public static Color ParseHex(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        string clean = hex.Trim().TrimStart('#');
        if (clean.Length == 6 && int.TryParse(clean, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
        {
            byte r = (byte)((rgb >> 16) & 0xFF);
            byte g = (byte)((rgb >> 8) & 0xFF);
            byte b = (byte)(rgb & 0xFF);
            return Color.FromRgb(r, g, b);
        }
        return fallback;
    }

    public static string ToHex(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
