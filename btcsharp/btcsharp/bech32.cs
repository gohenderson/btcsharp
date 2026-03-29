// Copyright (c) 2017 Pieter Wuille
// Copyright (c) 2021-2022 The Bitcoin Core developers
// Distributed under the MIT software license, see the accompanying
// file COPYING or http://www.opensource.org/licenses/mit-license.php.
//
// C# port of bitcoin/src/bech32.h and bech32.cpp
// See BIP173 (Bech32) and BIP350 (Bech32m).

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace BtcSharp;

public enum Encoding { Invalid, Bech32, Bech32m }

public sealed record DecodeResult(Encoding Encoding, string Hrp, byte[] Data)
{
    /// Sentinel returned for any failure; mirrors C++ DecodeResult default constructor.
    public static readonly DecodeResult Invalid = new(Encoding.Invalid, string.Empty, Array.Empty<byte>());
}

public static class Bech32
{
    private const int ChecksumSize = 6;
    private const char Separator = '1';

    /// BIP173/350 character limit for Bech32(m) encoded addresses.
    /// Guarantees detection of up to 4 errors within the window.
    public const int CharLimit = 90;

    /// The Bech32/Bech32m character set for encoding (all lowercase).
    private static readonly string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";

    /// Decode table: ASCII index → 5-bit value (0–31), or −1 for invalid.
    /// Both lowercase and uppercase variants of Charset characters are accepted.
    private static readonly sbyte[] CharsetRev = new sbyte[128];

    /// GF(1024) exponent table: GF1024Exp[k] = e^k where e is a primitive root.
    private static readonly int[] GF1024Exp = new int[1023];

    /// GF(1024) logarithm table: GF1024Log[v] = k such that e^k = v, or −1 for v=0.
    private static readonly int[] GF1024Log = new int[1024];

    /// Precomputed syndrome constants derived from the BCH generator roots
    /// e^997, e^998, e^999 in GF(1024).
    private static readonly uint[] SyndromeConsts = new uint[25];

    static Bech32()
    {
        // --- CharsetRev ---
        Array.Fill(CharsetRev, (sbyte)(-1));
        for (int i = 0; i < Charset.Length; i++)
        {
            CharsetRev[Charset[i]] = (sbyte)i;
            CharsetRev[char.ToUpperInvariant(Charset[i])] = (sbyte)i;
        }

        // --- GF(32) tables (used only during static init to build GF(1024)) ---
        // GF(32) is defined over GF(2) by the polynomial x^5 + x^3 + 1 (fmod = 41).
        var gf32Exp = new int[31];
        var gf32Log = new int[32];
        Array.Fill(gf32Log, -1);
        const int fmod = 41;
        gf32Exp[0] = 1;
        gf32Log[1] = 0;
        int v = 1;
        for (int i = 1; i < 31; i++)
        {
            v <<= 1;
            if ((v & 32) != 0) v ^= fmod;
            gf32Exp[i] = v;
            gf32Log[v] = i;
        }

        // --- GF(1024) tables ---
        // GF(1024) is a degree-2 extension of GF(32): x^2 + 9x + 23 over GF(32).
        // Every non-zero element is a power of the primitive root e = (1||0) = 32.
        Array.Fill(GF1024Log, -1);
        GF1024Exp[0] = 1;
        GF1024Log[1] = 0;
        v = 1;
        for (int i = 1; i < 1023; i++)
        {
            // Represent v as v1||v0 (two 5-bit GF(32) elements).
            // Multiplication by e: v0' = 23*v1, v1' = 9*v1 + v0 (all in GF(32)).
            int v0 = v & 31;
            int v1 = v >> 5;
            int v0n = v1 != 0 ? gf32Exp[(gf32Log[v1] + gf32Log[23]) % 31] : 0;
            int v1n = (v1 != 0 ? gf32Exp[(gf32Log[v1] + gf32Log[9]) % 31] : 0) ^ v0;
            v = (v1n << 5) | v0n;
            GF1024Exp[i] = v;
            GF1024Log[v] = i;
        }

        // --- Syndrome constants ---
        // For each position coefficient k (1..5) and each bit shift (0..4), precompute
        // the packed GF(1024) values used to evaluate the syndrome polynomial.
        for (int k = 1; k < 6; k++)
        {
            for (int shift = 0; shift < 5; shift++)
            {
                int b = GF1024Log[1 << shift];
                int c0 = GF1024Exp[(997 * k + b) % 1023];
                int c1 = GF1024Exp[(998 * k + b) % 1023];
                int c2 = GF1024Exp[(999 * k + b) % 1023];
                SyndromeConsts[5 * (k - 1) + shift] = (uint)c2 << 20 | (uint)c1 << 10 | (uint)c0;
            }
        }
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    private static uint EncodingConstant(Encoding encoding)
        => encoding == Encoding.Bech32 ? 1u : 0x2bc830a3u;

    /// Check for invalid or mixed-case characters. Appends offending positions to
    /// <paramref name="errors"/>. Returns true when no errors were found.
    private static bool CheckCharacters(string str, List<int> errors)
    {
        bool lower = false, upper = false;
        for (int i = 0; i < str.Length; i++)
        {
            char c = str[i];
            if (c >= 'a' && c <= 'z')
            {
                if (upper) errors.Add(i); else lower = true;
            }
            else if (c >= 'A' && c <= 'Z')
            {
                if (lower) errors.Add(i); else upper = true;
            }
            else if (c < 33 || c > 126) errors.Add(i);
        }
        return errors.Count == 0;
    }

    /// Verify the checksum embedded in <paramref name="values"/> (which includes
    /// the 6 checksum bytes). Returns the detected encoding, or Encoding.Invalid.
    private static Encoding VerifyChecksum(string hrp, ReadOnlySpan<byte> values)
    {
        var enc = PreparePolynomialCoefficients(hrp, values);
        uint check = PolyMod(enc);
        if (check == EncodingConstant(Encoding.Bech32))  return Encoding.Bech32;
        if (check == EncodingConstant(Encoding.Bech32m)) return Encoding.Bech32m;
        return Encoding.Invalid;
    }

    /// Compute the three BCH syndrome values s_997, s_998, s_999, each packed
    /// into 10 bits of the returned 30-bit integer.
    private static uint Syndrome(uint residue)
    {
        uint low = residue & 0x1f;
        uint result = low ^ (low << 10) ^ (low << 20);
        for (int i = 0; i < 25; i++)
            result ^= ((residue >> (5 + i)) & 1u) != 0 ? SyndromeConsts[i] : 0u;
        return result;
    }

    // -------------------------------------------------------------------------
    // Public API (mirrors bech32.h)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Compute the BCH polymod remainder over GF(32).
    /// Input is a sequence of 5-bit values; returns a 30-bit packed remainder.
    /// </summary>
    public static uint PolyMod(ReadOnlySpan<byte> v)
    {
        uint c = 1;
        foreach (var vi in v)
        {
            uint c0 = c >> 25;
            c = ((c & 0x1ffffffu) << 5) ^ vi;
            if ((c0 &  1) != 0) c ^= 0x3b6a57b2u;
            if ((c0 &  2) != 0) c ^= 0x26508e6du;
            if ((c0 &  4) != 0) c ^= 0x1ea119fau;
            if ((c0 &  8) != 0) c ^= 0x3d4233ddu;
            if ((c0 & 16) != 0) c ^= 0x2a1462b3u;
        }
        return c;
    }

    /// <summary>
    /// Expand the HRP and append payload values to form the polynomial coefficients
    /// used by PolyMod. Layout: (hrp high-bits) + 0 + (hrp low-bits) + data.
    /// </summary>
    public static byte[] PreparePolynomialCoefficients(string hrp, ReadOnlySpan<byte> values)
    {
        var ret = new List<byte>(hrp.Length + 1 + hrp.Length + values.Length + ChecksumSize);
        for (int i = 0; i < hrp.Length; i++) ret.Add((byte)((byte)hrp[i] >> 5));
        ret.Add(0);
        for (int i = 0; i < hrp.Length; i++) ret.Add((byte)((byte)hrp[i] & 0x1f));
        for (int i = 0; i < values.Length; i++) ret.Add(values[i]);
        return CollectionsMarshal.AsSpan(ret).ToArray();
    }

    /// <summary>Create the 6-symbol checksum for (encoding, hrp, data).</summary>
    public static byte[] CreateChecksum(Encoding encoding, string hrp, ReadOnlySpan<byte> values)
    {
        var enc = PreparePolynomialCoefficients(hrp, values);
        Array.Resize(ref enc, enc.Length + ChecksumSize);
        uint mod = PolyMod(enc) ^ EncodingConstant(encoding);
        var ret = new byte[ChecksumSize];
        for (int i = 0; i < ChecksumSize; i++)
            ret[i] = (byte)((mod >> (5 * (5 - i))) & 31u);
        return ret;
    }

    /// <summary>
    /// Encode a Bech32 or Bech32m string.
    /// HRP must be all lowercase (asserted); encoding must not be Invalid.
    /// </summary>
    public static string Encode(Encoding encoding, string hrp, ReadOnlySpan<byte> values)
    {
        Debug.Assert(encoding == Encoding.Bech32 || encoding == Encoding.Bech32m,
            "Encoding must be Bech32 or Bech32m");
        Debug.Assert(!hrp.Any(c => c is >= 'A' and <= 'Z'),
            "HRP must be lowercase");

        var sb = new StringBuilder(hrp.Length + 1 + values.Length + ChecksumSize);
        sb.Append(hrp);
        sb.Append(Separator);
        foreach (byte b in values)  sb.Append(Charset[b]);
        foreach (byte b in CreateChecksum(encoding, hrp, values)) sb.Append(Charset[b]);
        return sb.ToString();
    }

    /// <summary>
    /// Decode a Bech32 or Bech32m string.
    /// Returns <see cref="DecodeResult.Invalid"/> on any failure.
    /// </summary>
    public static DecodeResult Decode(string str, int limit = CharLimit)
    {
        var errors = new List<int>();
        if (!CheckCharacters(str, errors)) return DecodeResult.Invalid;
        if (str.Length > limit) return DecodeResult.Invalid;

        int pos = str.LastIndexOf(Separator);
        if (pos <= 0 || pos + ChecksumSize >= str.Length) return DecodeResult.Invalid;

        int dataLen = str.Length - 1 - pos;
        var values = new byte[dataLen];
        for (int i = 0; i < dataLen; i++)
        {
            char c = str[i + pos + 1];
            if (c >= 128) return DecodeResult.Invalid;
            sbyte rev = CharsetRev[c];
            if (rev == -1) return DecodeResult.Invalid;
            values[i] = (byte)rev;
        }

        string hrp = str[..pos].ToLowerInvariant();
        Encoding result = VerifyChecksum(hrp, values);
        if (result == Encoding.Invalid) return DecodeResult.Invalid;

        return new DecodeResult(result, hrp, values[..^ChecksumSize]);
    }

    /// <summary>
    /// Return the error message and positions of errors in a Bech32/Bech32m string.
    /// An empty error string means no errors were found.
    /// </summary>
    public static (string Error, List<int> ErrorLocations) LocateErrors(string str, int limit = CharLimit)
    {
        var errorLocations = new List<int>();

        if (str.Length > limit)
        {
            for (int i = limit; i < str.Length; i++) errorLocations.Add(i);
            return ("Bech32 string too long", errorLocations);
        }

        if (!CheckCharacters(str, errorLocations))
            return ("Invalid character or mixed case", errorLocations);

        int pos = str.LastIndexOf(Separator);
        if (pos < 0) return ("Missing separator", new List<int>());
        if (pos == 0 || pos + ChecksumSize >= str.Length)
        {
            errorLocations.Add(pos);
            return ("Invalid separator position", errorLocations);
        }

        string hrp = str[..pos].ToLowerInvariant();

        int length = str.Length - 1 - pos;
        var values = new byte[length];
        for (int i = pos + 1; i < str.Length; i++)
        {
            char c = str[i];
            if (c >= 128 || CharsetRev[c] == -1)
            {
                errorLocations.Add(i);
                return ("Invalid Base 32 character", errorLocations);
            }
            values[i - pos - 1] = (byte)CharsetRev[c];
        }

        Encoding? errorEncoding = null;

        foreach (var encoding in new[] { Encoding.Bech32, Encoding.Bech32m })
        {
            var possibleErrors = new List<int>();
            var enc = PreparePolynomialCoefficients(hrp, values);
            uint residue = PolyMod(enc) ^ EncodingConstant(encoding);

            if (residue == 0)
                return (string.Empty, new List<int>());

            uint syn = Syndrome(residue);
            int s0 = (int)(syn         & 0x3FF);
            int s1 = (int)((syn >> 10) & 0x3FF);
            int s2 = (int)(syn >> 20);

            int l_s0 = GF1024Log[s0];
            int l_s1 = GF1024Log[s1];
            int l_s2 = GF1024Log[s2];

            // Test for a single error: s1^2 == s0*s2 iff there is exactly one error.
            if (l_s0 != -1 && l_s1 != -1 && l_s2 != -1 &&
                (2 * l_s1 - l_s2 - l_s0 + 2046) % 1023 == 0)
            {
                int p1 = (l_s1 - l_s0 + 1023) % 1023;
                int l_e1 = l_s0 + (1023 - 997) * p1;
                if (p1 < length && l_e1 % 33 == 0)
                    possibleErrors.Add(str.Length - p1 - 1);
            }
            else
            {
                // Test for two errors by iterating over all possible first positions.
                for (int p1 = 0; p1 < length; p1++)
                {
                    int s2_s1p1 = s2 ^ (s1 == 0 ? 0 : GF1024Exp[(l_s1 + p1) % 1023]);
                    if (s2_s1p1 == 0) continue;
                    int l_s2_s1p1 = GF1024Log[s2_s1p1];

                    int s1_s0p1 = s1 ^ (s0 == 0 ? 0 : GF1024Exp[(l_s0 + p1) % 1023]);
                    if (s1_s0p1 == 0) continue;
                    int l_s1_s0p1 = GF1024Log[s1_s0p1];

                    int p2 = (l_s2_s1p1 - l_s1_s0p1 + 1023) % 1023;
                    if (p2 >= length || p1 == p2) continue;

                    int s1_s0p2 = s1 ^ (s0 == 0 ? 0 : GF1024Exp[(l_s0 + p2) % 1023]);
                    if (s1_s0p2 == 0) continue;
                    int l_s1_s0p2 = GF1024Log[s1_s0p2];

                    int inv_p1_p2 = 1023 - GF1024Log[GF1024Exp[p1] ^ GF1024Exp[p2]];

                    int l_e2 = l_s1_s0p1 + inv_p1_p2 + (1023 - 997) * p2;
                    if (l_e2 % 33 != 0) continue;

                    int l_e1 = l_s1_s0p2 + inv_p1_p2 + (1023 - 997) * p1;
                    if (l_e1 % 33 != 0) continue;

                    // Report positions left-to-right in the original string.
                    if (p1 > p2)
                    {
                        possibleErrors.Add(str.Length - p1 - 1);
                        possibleErrors.Add(str.Length - p2 - 1);
                    }
                    else
                    {
                        possibleErrors.Add(str.Length - p2 - 1);
                        possibleErrors.Add(str.Length - p1 - 1);
                    }
                    break;
                }
            }

            // Keep the candidate set with the fewest errors.
            if (errorLocations.Count == 0 ||
                (possibleErrors.Count > 0 && possibleErrors.Count < errorLocations.Count))
            {
                errorLocations = possibleErrors;
                if (errorLocations.Count > 0) errorEncoding = encoding;
            }
        }

        string errorMessage = errorEncoding == Encoding.Bech32m ? "Invalid Bech32m checksum"
                            : errorEncoding == Encoding.Bech32  ? "Invalid Bech32 checksum"
                            : "Invalid checksum";

        return (errorMessage, errorLocations);
    }
}
