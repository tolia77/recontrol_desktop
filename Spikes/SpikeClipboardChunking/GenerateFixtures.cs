using System.Text;

internal static class GenerateFixtures
{
    private const int Utf8CapBytes = 2_000_000;

    public static byte[] AsciiHappyPath() => Encoding.UTF8.GetBytes(new string('A', 1_990_000));

    public static byte[] AsciiOneAndAHalf() => Encoding.UTF8.GetBytes(new string('A', 1_500_000));

    public static byte[] AsciiNearHalf() => Encoding.UTF8.GetBytes(new string('A', 990_000));

    public static byte[] CyrillicOverCap()
    {
        var rng = new Random(13);
        var chars = new char[1_500_000];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = (char)(0x0400 + rng.Next(0x0100));
        }

        var bytes = Encoding.UTF8.GetBytes(chars);
        if (bytes.Length <= Utf8CapBytes)
        {
            throw new InvalidOperationException("CyrillicOverCap fixture must exceed the 2 MB UTF-8 cap.");
        }

        return bytes;
    }

    public static byte[] ZwjRtlNearCap()
    {
        const int targetBytes = 1_990_000;
        const string family = "\U0001F468\u200D\U0001F469\u200D\U0001F467\u200D\U0001F466";
        const string hebrewWord = "שלום";
        const string arabicWord = "سلام";
        const string hebrewChar = "ש";
        const string zwj = "\u200D";
        const string emojiMan = "\U0001F468";

        var chunks = new[]
        {
            Encoding.UTF8.GetBytes(family),
            Encoding.UTF8.GetBytes(hebrewWord),
            Encoding.UTF8.GetBytes(arabicWord),
            Encoding.UTF8.GetBytes(hebrewChar),
            Encoding.UTF8.GetBytes(zwj),
            Encoding.UTF8.GetBytes(emojiMan),
            Encoding.UTF8.GetBytes("A"),
        };

        var result = new List<byte>(targetBytes + 64);
        var cycle = 0;

        while (result.Count < targetBytes)
        {
            var candidate = chunks[cycle % chunks.Length];
            cycle++;

            if (result.Count + candidate.Length <= targetBytes)
            {
                result.AddRange(candidate);
            }
        }

        if (result.Count != targetBytes)
        {
            throw new InvalidOperationException("ZwjRtlNearCap must be exactly 1,990,000 UTF-8 bytes.");
        }

        return result.ToArray();
    }

    public static IReadOnlyDictionary<string, Func<byte[]>> Matrix() =>
        new Dictionary<string, Func<byte[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["AsciiHappyPath"] = AsciiHappyPath,
            ["AsciiOneAndAHalf"] = AsciiOneAndAHalf,
            ["AsciiNearHalf"] = AsciiNearHalf,
            ["CyrillicOverCap"] = CyrillicOverCap,
            ["ZwjRtlNearCap"] = ZwjRtlNearCap,
        };
}
