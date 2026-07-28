using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskbarTunes.Helpers;

/// <summary>
/// Extrae un color dominante «vivo» de la carátula (media ponderada por
/// saturación×luminancia) y deriva un color secundario para el degradado.
/// </summary>
public static class ColorExtractor
{
    public static (Color Main, Color Secondary)? FromBitmap(BitmapSource source)
    {
        try
        {
            var bmp = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            if (w == 0 || h == 0) return null;

            var pixels = new byte[w * h * 4];
            bmp.CopyPixels(pixels, w * 4, 0);

            double r = 0, g = 0, b = 0, weightSum = 0;
            for (int i = 0; i < pixels.Length; i += 8) // muestrea 1 de cada 2 px
            {
                byte pb = pixels[i], pg = pixels[i + 1], pr = pixels[i + 2], pa = pixels[i + 3];
                if (pa < 128) continue;

                double max = Math.Max(pr, Math.Max(pg, pb)) / 255.0;
                double min = Math.Min(pr, Math.Min(pg, pb)) / 255.0;
                double sat = max <= 0 ? 0 : (max - min) / max;

                // Peso: favorece píxeles saturados y luminosos; el +0.02 evita
                // división por cero en carátulas en blanco y negro.
                double weight = sat * sat * max + 0.02;
                r += pr * weight; g += pg * weight; b += pb * weight;
                weightSum += weight;
            }
            if (weightSum <= 0) return null;

            var (hue, s, v) = RgbToHsv(r / weightSum / 255, g / weightSum / 255, b / weightSum / 255);

            // Realza para que luzca sobre la barra de tareas
            s = Math.Max(s, 0.55);
            v = Math.Max(v, 0.85);
            var main = HsvToColor(hue, s, v);
            var secondary = HsvToColor((hue + 40) % 360, Math.Max(s - 0.1, 0.45), Math.Min(v + 0.1, 1));

            return (main, secondary);
        }
        catch
        {
            return null;
        }
    }

    public static (double H, double S, double V) RgbToHsv(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max <= 0 ? 0 : d / max, max);
    }

    public static Color HsvToColor(double h, double s, double v, byte alpha = 255)
    {
        double c = v * s;
        double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        double m = v - c;
        (double r, double g, double b) = ((int)(h / 60) % 6) switch
        {
            0 => (c, x, 0.0),
            1 => (x, c, 0.0),
            2 => (0.0, c, x),
            3 => (0.0, x, c),
            4 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return Color.FromArgb(alpha,
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }
}
