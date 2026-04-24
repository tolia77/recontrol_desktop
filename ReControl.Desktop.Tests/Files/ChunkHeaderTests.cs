using System;
using FluentAssertions;
using ReControl.Desktop.Services.Files.FilesProtocol;
using Xunit;

namespace ReControl.Desktop.Tests.Files;

/// <summary>
/// Parity tests for <see cref="ChunkHeader"/>.
///
/// The test vector below is SHARED with the TypeScript side
/// (recontrol_frontend/src/pages/DeviceControl/services/files/ChunkHeader.ts).
/// Any change here requires the same change there.
/// </summary>
public class ChunkHeaderTests
{
    // Hex: 78 56 34 12  DD CC BB AA  EF CD AB 89 67 45 23 01
    //      transferId     seq              offset (u64 LE)
    //      = 0x12345678   = 0xAABBCCDD     = 0x0123456789ABCDEF
    private static readonly byte[] TestVector =
        Convert.FromHexString("78563412DDCCBBAAEFCDAB8967452301");

    private static readonly ChunkHeader TestValues =
        new(TransferId: 0x12345678u, Seq: 0xAABBCCDDu, Offset: 0x0123456789ABCDEFul);

    [Fact]
    public void Read_TestVector_Matches_ExpectedValues()
    {
        var parsed = ChunkHeader.Read(TestVector);

        parsed.TransferId.Should().Be(0x12345678u);
        parsed.Seq.Should().Be(0xAABBCCDDu);
        parsed.Offset.Should().Be(0x0123456789ABCDEFul);
    }

    [Fact]
    public void Write_ExpectedValues_Produces_TestVector()
    {
        var buf = new byte[ChunkHeader.Size];

        TestValues.WriteTo(buf);

        buf.Should().Equal(TestVector);
    }

    [Fact]
    public void RoundTrip_IsLossless()
    {
        var original = new ChunkHeader(1234u, 5678u, 0xFEDCBA9876543210ul);
        var buf = new byte[ChunkHeader.Size];

        original.WriteTo(buf);
        var roundTripped = ChunkHeader.Read(buf);

        roundTripped.Should().Be(original);
    }

    [Fact]
    public void Read_TooShort_Throws()
    {
        Action act = () => ChunkHeader.Read(new byte[ChunkHeader.Size - 1]);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Size_Constant_Is_16()
    {
        ChunkHeader.Size.Should().Be(16);
    }
}
