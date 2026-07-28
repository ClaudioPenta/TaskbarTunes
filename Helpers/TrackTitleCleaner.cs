using System.Text.RegularExpressions;

namespace TaskbarTunes.Helpers;

/// <summary>
/// Limpia los títulos que llegan desde el navegador (YouTube, etc.):
/// quita "(Official Video)", "[HD]", sufijos " - Topic" y separa "Artista - Título".
/// </summary>
public static partial class TrackTitleCleaner
{
    private static readonly Regex YouTubeSuffix = new(@"\s*-\s*YouTube(\s+Music)?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BracketNoise = new(
        @"\s*[\(\[][^\)\]]*(official|oficial|video|lyric|letra|audio|visuali[sz]er|remaster|hd|4k|mv|m/v|sub(t[ií]tulos|s)?|videoclip|live|en vivo|explicit)[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex TopicSuffix = new(@"\s*-\s*Topic\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ArtistDashTitle = new(@"^(?<artist>[^\-–—|]{1,60}?)\s*[\-–—|]\s*(?<title>.{2,})$",
        RegexOptions.Compiled);

    private static readonly char[] TrimChars = { ' ', '-', '–', '—', '|', '"', '“', '”' };

    public static (string Title, string Artist) Clean(string title, string artist)
    {
        title = YouTubeSuffix.Replace(title ?? "", "");
        title = BracketNoise.Replace(title, "");
        title = Regex.Replace(title, @"\s{2,}", " ").Trim(TrimChars);

        artist = TopicSuffix.Replace(artist ?? "", "").Trim(TrimChars);

        // "Artista - Título": el canal suele ser ruido (p.ej. "xVEVO"), preferimos el split.
        var m = ArtistDashTitle.Match(title);
        if (m.Success)
        {
            var left = m.Groups["artist"].Value.Trim(TrimChars);
            var rest = m.Groups["title"].Value.Trim(TrimChars);

            // "CANAL - Artista - Título": si el prefijo repite el canal, separar otra vez
            if (left.Equals(artist, StringComparison.OrdinalIgnoreCase))
            {
                var m2 = ArtistDashTitle.Match(rest);
                if (m2.Success)
                {
                    left = m2.Groups["artist"].Value.Trim(TrimChars);
                    rest = m2.Groups["title"].Value.Trim(TrimChars);
                }
            }

            title = rest;
            artist = left;
        }

        return (title, artist);
    }
}
