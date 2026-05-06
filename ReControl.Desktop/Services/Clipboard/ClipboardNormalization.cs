using System.Globalization;

namespace ReControl.Desktop.Services.Clipboard;

public static class ClipboardNormalization
{
    public const double NonTextThreshold = 0.20;

    public static (string Text, bool Refused) Normalize(string raw)
    {
        // Step 1 (D-13): strip embedded NUL bytes
        var stripped = raw.Replace("\0", string.Empty);
        // Step 2 (D-13, CLIP-07): CRLF then lone CR -> LF
        var lf = stripped.Replace("\r\n", "\n").Replace("\r", "\n");
        // Step 3 (D-13, CLIP-08): >20% control chars (excluding \t \n \r) -> refuse
        if (lf.Length == 0) return (lf, false);
        int control = 0;
        foreach (var ch in lf)
        {
            if (ch == '\t' || ch == '\n' || ch == '\r') continue;
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.Control) control++;
        }
        var ratio = (double)control / lf.Length;
        return ratio > NonTextThreshold ? (lf, true) : (lf, false);
    }
}
