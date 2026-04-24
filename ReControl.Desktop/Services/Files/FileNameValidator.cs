using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace ReControl.Desktop.Services.Files;

/// <summary>
/// Validates a single file or folder name (not a path) against:
///   - Windows reserved device names (CON, PRN, AUX, NUL, COM0-9, COM superscript 1-3, LPT0-9, LPT superscript 1-3) -- with or without extension.
///   - OS-invalid characters (Path.GetInvalidFileNameChars).
///   - Length (&gt; 255 bytes).
///   - Empty / dot-only names.
///   - Trailing space or dot (invalid on Windows).
///
/// Rules are applied unconditionally on all platforms: a Linux desktop may share
/// files with Windows peers via Phase 11 transfer, and names that round-trip
/// through Windows must be valid there too.
/// </summary>
public static class FileNameValidator
{
    // Matches CON, PRN, AUX, NUL, COM[0-9¹²³], LPT[0-9¹²³], with optional single extension.
    // Anchored so the whole name must match; case-insensitive for ASCII.
    private static readonly Regex ReservedRegex = new(
        @"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(\.[^.]*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    /// <summary>
    /// Throws InvalidFileNameException (with a specific Reason subfield) if the name
    /// is not acceptable. Returns silently when valid.
    /// </summary>
    public static void Validate(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new InvalidFileNameException(name ?? "", "EMPTY");
        if (name.Length > 255)
            throw new InvalidFileNameException(name, "TOO_LONG");
        if (name.All(c => c == '.'))
            throw new InvalidFileNameException(name, "DOT_ONLY");
        if (name.EndsWith(' ') || name.EndsWith('.'))
            throw new InvalidFileNameException(name, "TRAILING_SPACE_OR_DOT");
        if (name.IndexOfAny(InvalidChars) >= 0)
            throw new InvalidFileNameException(name, "ILLEGAL_CHAR");
        if (ReservedRegex.IsMatch(name))
            throw new InvalidFileNameException(name, "RESERVED");
    }
}
