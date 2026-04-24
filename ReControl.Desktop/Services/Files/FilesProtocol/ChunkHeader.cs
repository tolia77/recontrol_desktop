using System;
using System.Buffers.Binary;

namespace ReControl.Desktop.Services.Files.FilesProtocol;

/// <summary>
/// 16-byte binary header for files-data chunks.
/// Layout (all little-endian):
///   bytes  0..3  : transferId (u32)
///   bytes  4..7  : seq        (u32)
///   bytes  8..15 : offset     (u64)
/// Transfer-end signaling is via files-ctl ack (NOT a flag bit).
///
/// See <c>recontrol_desktop/protocol/files-data.md</c> and
/// <c>.planning/phases/09-backend-foundation/09-SPIKE-FINDINGS.md</c>.
/// The TypeScript counterpart is at
/// <c>recontrol_frontend/src/pages/DeviceControl/services/files/ChunkHeader.ts</c> --
/// both sides MUST produce byte-identical output for a given triple.
/// </summary>
public readonly record struct ChunkHeader(uint TransferId, uint Seq, ulong Offset)
{
    /// <summary>Header size in bytes. Always 16.</summary>
    public const int Size = 16;

    /// <summary>
    /// Writes this header into <paramref name="dst"/> starting at offset 0.
    /// Throws <see cref="ArgumentException"/> if <paramref name="dst"/> has fewer than 16 bytes.
    /// </summary>
    public void WriteTo(Span<byte> dst)
    {
        if (dst.Length < Size)
            throw new ArgumentException($"Need at least {Size} bytes", nameof(dst));

        BinaryPrimitives.WriteUInt32LittleEndian(dst[..4], TransferId);
        BinaryPrimitives.WriteUInt32LittleEndian(dst.Slice(4, 4), Seq);
        BinaryPrimitives.WriteUInt64LittleEndian(dst.Slice(8, 8), Offset);
    }

    /// <summary>
    /// Reads a header from <paramref name="src"/> starting at offset 0.
    /// Throws <see cref="ArgumentException"/> if <paramref name="src"/> has fewer than 16 bytes.
    /// </summary>
    public static ChunkHeader Read(ReadOnlySpan<byte> src)
    {
        if (src.Length < Size)
            throw new ArgumentException($"Need at least {Size} bytes", nameof(src));

        return new ChunkHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(src[..4]),
            BinaryPrimitives.ReadUInt32LittleEndian(src.Slice(4, 4)),
            BinaryPrimitives.ReadUInt64LittleEndian(src.Slice(8, 8)));
    }
}
