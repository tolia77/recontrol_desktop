using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;

namespace ReControl.Desktop.Tests.Clipboard;

public class ClipboardHashSymmetryTests
{
    [Fact]
    public void CanonicalFixtures_MatchExpectedHash16()
    {
        var fixturePath = FindFixturePath();
        using var doc = JsonDocument.Parse(File.ReadAllText(fixturePath));
        var fixtures = doc.RootElement.GetProperty("fixtures").EnumerateArray();

        foreach (var fixture in fixtures)
        {
            var name = fixture.GetProperty("name").GetString() ?? "<unknown>";
            var expected = fixture.GetProperty("expectedHash16").GetString() ?? string.Empty;
            var utf8Bytes = DecodeFixtureBytes(fixture);
            var actual = ComputeHash16(utf8Bytes);
            actual.Should().Be(expected, $"fixture {name} should match expectedHash16");
        }
    }

    private static string FindFixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "recontrol_desktop", "protocol", "test-fixtures", "clipboard-hash-fixtures.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("clipboard-hash-fixtures.json not found");
    }

    private static byte[] DecodeFixtureBytes(JsonElement fixture)
    {
        if (fixture.TryGetProperty("utf8Hex", out var utf8Hex))
            return Convert.FromHexString(utf8Hex.GetString() ?? string.Empty);

        var patternHex = fixture.GetProperty("utf8Pattern").GetString() ?? string.Empty;
        var repeatCount = fixture.GetProperty("utf8RepeatCount").GetInt32();
        var pattern = Convert.FromHexString(patternHex);
        var bytes = new byte[pattern.Length * repeatCount];
        for (var i = 0; i < repeatCount; i++)
            Buffer.BlockCopy(pattern, 0, bytes, i * pattern.Length, pattern.Length);
        return bytes;
    }

    private static string ComputeHash16(byte[] utf8Bytes)
    {
        var digest = SHA256.HashData(utf8Bytes);
        return Convert.ToHexString(digest.AsSpan(0, 8)).ToLowerInvariant();
    }
}
