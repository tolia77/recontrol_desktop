using FluentAssertions;
using ReControl.Desktop.Services.Files;

namespace ReControl.Desktop.Tests.Files;

public class FileNameValidatorTests
{
    [Fact]
    public void EmptyName_Rejected_Empty()
    {
        var ex = Assert.Throws<InvalidFileNameException>(() => FileNameValidator.Validate(""));
        ex.Reason.Should().Be("EMPTY");
    }

    [Fact]
    public void TooLongName_Rejected_TooLong()
    {
        var name = new string('a', 256);
        var ex = Assert.Throws<InvalidFileNameException>(() => FileNameValidator.Validate(name));
        ex.Reason.Should().Be("TOO_LONG");
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("....")]
    public void DotOnlyName_Rejected_DotOnly(string name)
    {
        var ex = Assert.Throws<InvalidFileNameException>(() => FileNameValidator.Validate(name));
        ex.Reason.Should().Be("DOT_ONLY");
    }

    [Theory]
    [InlineData("foo.")]
    [InlineData("foo ")]
    [InlineData("bar.txt.")]
    [InlineData("bar.txt ")]
    public void TrailingSpaceOrDot_Rejected(string name)
    {
        var ex = Assert.Throws<InvalidFileNameException>(() => FileNameValidator.Validate(name));
        ex.Reason.Should().Be("TRAILING_SPACE_OR_DOT");
    }

    [Theory]
    [InlineData("foo/bar.txt")]
    [InlineData("foo\0bar")]
    public void IllegalChar_Rejected(string name)
    {
        var ex = Assert.Throws<InvalidFileNameException>(() => FileNameValidator.Validate(name));
        ex.Reason.Should().Be("ILLEGAL_CHAR");
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT9")]
    [InlineData("CON.txt")]
    [InlineData("NUL.txt")]
    [InlineData("com1.TXT")]
    [InlineData("COM¹")] // COM superscript 1
    [InlineData("COM²")] // COM superscript 2
    [InlineData("COM³")] // COM superscript 3
    [InlineData("LPT¹")] // LPT superscript 1
    public void ReservedName_Rejected(string name)
    {
        var ex = Assert.Throws<InvalidFileNameException>(() => FileNameValidator.Validate(name));
        ex.Reason.Should().Be("RESERVED");
    }

    [Theory]
    [InlineData("normal.txt")]
    [InlineData("folder-name")]
    [InlineData("my file.pdf")]
    [InlineData("COM10")]  // NOT reserved: regex matches only COM0-9 (single digit) and COM superscript 1-3
    [InlineData("COMA")]   // not a digit
    [InlineData("LPT10")]  // same: LPT0-9 is single digit only
    [InlineData("a.b.c")]
    [InlineData("foo bar")]
    [InlineData("file")]
    public void ValidName_Accepted(string name)
    {
        FileNameValidator.Validate(name);
    }
}
