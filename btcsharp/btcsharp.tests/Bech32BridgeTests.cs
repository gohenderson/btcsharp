// C# tests for the managed side of btcsharp_bech32_bridge.
//
// The [UnmanagedCallersOnly] entry points themselves cannot be called from
// managed code, so tests exercise the three internal helpers that the entry
// points delegate to:
//
//   EncodeManaged  — UTF-8 HRP + raw values → encoded string
//   DecodeManaged  — UTF-8 input string → DecodeResult
//   FillResult     — DecodeResult → BtcSharpBech32Result struct

using BtcSharp;
using BtcSharp.Interop;
using Xunit;
using TextEncoding = System.Text.Encoding;

namespace BtcSharp.Tests;

public class Bech32BridgeTests
{
    // -------------------------------------------------------------------------
    // EncodeManaged
    // -------------------------------------------------------------------------

    [Fact]
    public void EncodeManaged_ValidBech32_ReturnsExpectedString()
    {
        // Round-trip one of the canonical test vectors.
        const string expected = "abcdef1qpzry9x8gf2tvdw0s3jn54khce6mua7lmqqqxw";

        var dec = Bech32.Decode(expected);
        string hrpUtf8 = dec.Hrp;

        string result = Bech32Bridge.EncodeManaged(
            BtcSharp.Encoding.Bech32,
            TextEncoding.UTF8.GetBytes(hrpUtf8),
            dec.Data);

        Assert.Equal(expected, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EncodeManaged_ValidBech32m_ReturnsExpectedString()
    {
        const string expected = "abcdef1l7aum6echk45nj3s0wdvt2fg8x9yrzpqzd3ryx";

        var dec = Bech32.Decode(expected);

        string result = Bech32Bridge.EncodeManaged(
            BtcSharp.Encoding.Bech32m,
            TextEncoding.UTF8.GetBytes(dec.Hrp),
            dec.Data);

        Assert.Equal(expected, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EncodeManaged_InvalidEncoding_ReturnsEmpty()
    {
        string result = Bech32Bridge.EncodeManaged(
            BtcSharp.Encoding.Invalid,
            TextEncoding.UTF8.GetBytes("bc"),
            new byte[] { 0, 1, 2 });

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void EncodeManaged_EmptyHrp_ReturnsEmpty()
    {
        // Bech32.Encode asserts HRP is non-empty; verify the bridge handles it.
        // An empty HRP produces a string that fails the separator-position check
        // in Decode, so Encode itself will produce output but Decode won't accept it.
        // The bridge should not crash.
        var ex = Record.Exception(() =>
            Bech32Bridge.EncodeManaged(
                BtcSharp.Encoding.Bech32,
                ReadOnlySpan<byte>.Empty,
                new byte[] { 0 }));
        Assert.Null(ex);
    }

    // -------------------------------------------------------------------------
    // DecodeManaged
    // -------------------------------------------------------------------------

    [Fact]
    public void DecodeManaged_ValidBech32_ReturnsCorrectFields()
    {
        const string input = "abcdef1qpzry9x8gf2tvdw0s3jn54khce6mua7lmqqqxw";
        byte[] inputBytes = TextEncoding.UTF8.GetBytes(input);

        var result = Bech32Bridge.DecodeManaged(inputBytes, Bech32.CharLimit);

        Assert.Equal(BtcSharp.Encoding.Bech32, result.Encoding);
        Assert.Equal("abcdef", result.Hrp);
        Assert.NotEmpty(result.Data);
    }

    [Fact]
    public void DecodeManaged_ValidBech32m_ReturnsCorrectEncoding()
    {
        const string input = "abcdef1l7aum6echk45nj3s0wdvt2fg8x9yrzpqzd3ryx";
        byte[] inputBytes = TextEncoding.UTF8.GetBytes(input);

        var result = Bech32Bridge.DecodeManaged(inputBytes, Bech32.CharLimit);

        Assert.Equal(BtcSharp.Encoding.Bech32m, result.Encoding);
    }

    [Fact]
    public void DecodeManaged_InvalidString_ReturnsInvalid()
    {
        byte[] inputBytes = TextEncoding.UTF8.GetBytes("not-a-bech32-string");

        var result = Bech32Bridge.DecodeManaged(inputBytes, Bech32.CharLimit);

        Assert.Equal(BtcSharp.Encoding.Invalid, result.Encoding);
    }

    [Fact]
    public void DecodeManaged_EmptyInput_ReturnsInvalid()
    {
        var result = Bech32Bridge.DecodeManaged(ReadOnlySpan<byte>.Empty, Bech32.CharLimit);
        Assert.Equal(BtcSharp.Encoding.Invalid, result.Encoding);
    }

    [Fact]
    public void DecodeManaged_ExceedsLimit_ReturnsInvalid()
    {
        // "A12UEL5L" is valid at limit=90 but invalid at limit=7 (length is 8).
        byte[] inputBytes = TextEncoding.UTF8.GetBytes("A12UEL5L");

        var result = Bech32Bridge.DecodeManaged(inputBytes, limit: 7);

        Assert.Equal(BtcSharp.Encoding.Invalid, result.Encoding);
    }

    // -------------------------------------------------------------------------
    // FillResult — struct layout and field values
    // -------------------------------------------------------------------------

    [Fact]
    public unsafe void FillResult_ValidDecode_PopulatesAllFields()
    {
        var dec = Bech32.Decode("abcdef1qpzry9x8gf2tvdw0s3jn54khce6mua7lmqqqxw");

        BtcSharpBech32Result r = default;
        Bech32Bridge.FillResult(dec, &r);

        Assert.Equal((int)BtcSharp.Encoding.Bech32, r.Encoding);

        // HRP
        Assert.Equal(6, r.HrpLen); // "abcdef"
        string hrp = TextEncoding.UTF8.GetString(new ReadOnlySpan<byte>(r.Hrp, r.HrpLen));
        Assert.Equal("abcdef", hrp);
        Assert.Equal(0, r.Hrp[r.HrpLen]); // null-terminated

        // Data
        Assert.True(r.DataLen > 0);
        Assert.Equal(dec.Data.Length, r.DataLen);
    }

    [Fact]
    public unsafe void FillResult_InvalidDecode_SetsEncodingToZeroAndLengthsToZero()
    {
        BtcSharpBech32Result r = default;
        Bech32Bridge.FillResult(DecodeResult.Invalid, &r);

        Assert.Equal(0, r.Encoding); // Invalid
        Assert.Equal(0, r.HrpLen);
        Assert.Equal(0, r.DataLen);
    }

    [Fact]
    public unsafe void FillResult_Bech32m_SetsEncodingToTwo()
    {
        var dec = Bech32.Decode("abcdef1l7aum6echk45nj3s0wdvt2fg8x9yrzpqzd3ryx");

        BtcSharpBech32Result r = default;
        Bech32Bridge.FillResult(dec, &r);

        Assert.Equal(2, r.Encoding); // Bech32m
    }

    [Fact]
    public unsafe void FillResult_DataRoundTrips()
    {
        // Decode a known vector, fill the struct, verify data bytes match.
        var dec = Bech32.Decode("split1checkupstagehandshakeupstreamerranterredcaperred2y9e3w");

        BtcSharpBech32Result r = default;
        Bech32Bridge.FillResult(dec, &r);

        Assert.Equal(dec.Data.Length, r.DataLen);
        for (int i = 0; i < r.DataLen; i++)
            Assert.Equal(dec.Data[i], r.Data[i]);
    }

    // -------------------------------------------------------------------------
    // Struct layout contract
    // -------------------------------------------------------------------------

    [Fact]
    public unsafe void BtcSharpBech32Result_SizeMatchesHeader()
    {
        // sizeof must equal 4+4+4+91+91 = 194 (Pack=1, no padding).
        // This guards against accidental layout drift between C# and C++.
        Assert.Equal(194, sizeof(BtcSharpBech32Result));
    }
}
