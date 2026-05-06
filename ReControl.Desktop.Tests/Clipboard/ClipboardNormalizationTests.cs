using FluentAssertions;
using ReControl.Desktop.Services.Clipboard;

namespace ReControl.Desktop.Tests.Clipboard;

public class ClipboardNormalizationTests
{
    [Fact]
    public void Normalize_StripsNulBytes()
    {
        var (text, refused) = ClipboardNormalization.Normalize("a\0b\0c");
        text.Should().Be("abc");
        refused.Should().BeFalse();
    }

    [Fact]
    public void Normalize_ConvertsCrlfAndCrToLf()
    {
        var (text, refused) = ClipboardNormalization.Normalize("line1\r\nline2\rline3");
        text.Should().Be("line1\nline2\nline3");
        refused.Should().BeFalse();
    }

    [Fact]
    public void Normalize_EmptyString()
    {
        var (text, refused) = ClipboardNormalization.Normalize(string.Empty);
        text.Should().Be(string.Empty);
        refused.Should().BeFalse();
    }

    [Fact]
    public void Normalize_PassesThroughPlainText()
    {
        var (text, refused) = ClipboardNormalization.Normalize("hello");
        text.Should().Be("hello");
        refused.Should().BeFalse();
    }

    [Fact]
    public void Normalize_RefusesHighControlRatio()
    {
        // 50 control chars + "ab" -> ~96% control
        var raw = new string('\x01', 50) + "ab";
        var (_, refused) = ClipboardNormalization.Normalize(raw);
        refused.Should().BeTrue();
    }

    [Fact]
    public void Normalize_AcceptsFifteenPercentControl()
    {
        // 85 letters + 15 control chars = 15% control
        var raw = new string('a', 85) + new string('\x01', 15);
        var (_, refused) = ClipboardNormalization.Normalize(raw);
        refused.Should().BeFalse();
    }

    [Fact]
    public void Normalize_RefusesTwentyFivePercentControl()
    {
        // 75 letters + 25 control chars = 25% control
        var raw = new string('a', 75) + new string('\x01', 25);
        var (_, refused) = ClipboardNormalization.Normalize(raw);
        refused.Should().BeTrue();
    }

    [Fact]
    public void Normalize_DoesNotCountTabLfCrAsControl()
    {
        var (_, refused) = ClipboardNormalization.Normalize("\t\n\r normal text");
        refused.Should().BeFalse();
    }

    [Fact]
    public void Normalize_PassesUnicodeText()
    {
        // Cyrillic + CJK + ZWJ family emoji
        var raw = "Привет 你好 👨‍👩";
        var (text, refused) = ClipboardNormalization.Normalize(raw);
        text.Should().Be(raw);
        refused.Should().BeFalse();
    }

    [Fact]
    public void Normalize_BoundaryExactlyTwentyPercentNotRefused()
    {
        // 80 letters + 20 control chars = exactly 20% control; strict > means accepted
        var raw = new string('a', 80) + new string('\x01', 20);
        var (_, refused) = ClipboardNormalization.Normalize(raw);
        refused.Should().BeFalse();
    }
}
