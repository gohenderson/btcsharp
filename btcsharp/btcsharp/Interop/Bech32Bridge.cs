// Copyright (c) 2021-2022 The Bitcoin Core developers
// Distributed under the MIT software license, see the accompanying
// file COPYING or http://www.opensource.org/licenses/mit-license.php.
//
// Managed side of the Bech32 interop bridge.
//
// The [UnmanagedCallersOnly] entry points are loaded by btcsharp_bech32_bridge.cpp
// via fxr_load_umco().  All logic lives in internal helpers so that tests can
// call them directly from managed code without hitting the
// [UnmanagedCallersOnly] restriction.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using TextEncoding = System.Text.Encoding;

namespace BtcSharp.Interop;

/// <summary>
/// Blittable result struct written by BtcSharp_Bech32_Decode and read by C++.
/// Layout must match btcsharp_bech32_result in btcsharp_bech32_bridge.h exactly.
/// Pack=1 is set on both sides to avoid any compiler-specific padding.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal unsafe struct BtcSharpBech32Result
{
    public int  Encoding;      // 0=Invalid  1=Bech32  2=Bech32m
    public int  HrpLen;        // byte length of Hrp (0 when Invalid)
    public int  DataLen;       // byte count of Data  (0 when Invalid)
    public fixed byte Hrp[91]; // UTF-8 HRP, null-terminated, max 90 chars + '\0'
    public fixed byte Data[91];// decoded payload bytes, max 91 bytes
}

internal static class Bech32Bridge
{
    // -------------------------------------------------------------------------
    // Internal helpers — testable from managed code
    // -------------------------------------------------------------------------

    /// Decode a UTF-8 HRP and encode values as Bech32/Bech32m.
    /// Returns the encoded string, or empty on any failure.
    internal static string EncodeManaged(
        BtcSharp.Encoding enc,
        ReadOnlySpan<byte> hrpUtf8,
        ReadOnlySpan<byte> values)
    {
        if (enc == BtcSharp.Encoding.Invalid) return string.Empty;
        string hrp = TextEncoding.UTF8.GetString(hrpUtf8);
        return Bech32.Encode(enc, hrp, values);
    }

    /// Decode a UTF-8 Bech32/Bech32m string.
    internal static DecodeResult DecodeManaged(ReadOnlySpan<byte> strUtf8, int limit)
    {
        string str = TextEncoding.UTF8.GetString(strUtf8);
        return Bech32.Decode(str, limit);
    }

    /// Fill a BtcSharpBech32Result from a managed DecodeResult.
    /// Separated from DecodeManaged so tests can verify the struct layout.
    internal static unsafe void FillResult(DecodeResult dec, BtcSharpBech32Result* r)
    {
        r->Encoding = (int)dec.Encoding;
        r->HrpLen   = 0;
        r->DataLen  = 0;

        if (dec.Encoding == BtcSharp.Encoding.Invalid) return;

        // Write null-terminated UTF-8 HRP into the fixed array.
        int hrpWritten = TextEncoding.UTF8.GetBytes(dec.Hrp, new Span<byte>(r->Hrp, 91));
        r->HrpLen      = hrpWritten;
        r->Hrp[hrpWritten] = 0;

        // Write raw data bytes.
        int dataLen = Math.Min(dec.Data.Length, 91);
        dec.Data.AsSpan(0, dataLen).CopyTo(new Span<byte>(r->Data, 91));
        r->DataLen = dataLen;
    }

    // -------------------------------------------------------------------------
    // [UnmanagedCallersOnly] entry points — loaded by btcsharp_bech32_bridge.cpp
    // -------------------------------------------------------------------------

    /// Encode values as Bech32/Bech32m and write the ASCII result into out_buf.
    /// On success *out_len is set to the number of bytes written (no null terminator).
    /// On failure *out_len is set to 0.
    [UnmanagedCallersOnly(EntryPoint = "BtcSharp_Bech32_Encode",
                          CallConvs = new[] { typeof(CallConvCdecl) })]
    public static unsafe void Encode(
        int    encoding,
        byte*  hrp,       int hrp_len,
        byte*  values,    int values_len,
        byte*  out_buf,   int out_cap,
        int*   out_len)
    {
        if (out_len != null) *out_len = 0;
        if (out_buf == null || out_cap <= 0) return;

        try
        {
            var enc    = (BtcSharp.Encoding)encoding;
            var hrpSpan    = hrp    != null ? new ReadOnlySpan<byte>(hrp,    hrp_len)    : ReadOnlySpan<byte>.Empty;
            var valSpan    = values != null ? new ReadOnlySpan<byte>(values, values_len) : ReadOnlySpan<byte>.Empty;

            string result = EncodeManaged(enc, hrpSpan, valSpan);
            if (result.Length == 0) return;

            int written = TextEncoding.UTF8.GetBytes(result.AsSpan(), new Span<byte>(out_buf, out_cap));
            if (out_len != null) *out_len = written;
        }
        catch { /* never propagate across the native boundary */ }
    }

    /// Decode a Bech32/Bech32m string and fill *result.
    /// result->Encoding is set to 0 (Invalid) on any failure.
    [UnmanagedCallersOnly(EntryPoint = "BtcSharp_Bech32_Decode",
                          CallConvs = new[] { typeof(CallConvCdecl) })]
    public static unsafe void Decode(
        byte*                  str,
        int                    str_len,
        int                    limit,
        BtcSharpBech32Result*  result)
    {
        if (result == null) return;
        *result = default;

        try
        {
            var strSpan = str != null ? new ReadOnlySpan<byte>(str, str_len) : ReadOnlySpan<byte>.Empty;
            var dec     = DecodeManaged(strSpan, limit);
            FillResult(dec, result);
        }
        catch { /* never propagate across the native boundary */ }
    }
}
