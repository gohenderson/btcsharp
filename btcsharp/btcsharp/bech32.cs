using System.Runtime.InteropServices;

namespace btcsharp;

public enum Encoding
{
    Bech32,
    Bech32m,
    Invalid
}

public static class Bech32
{
    private const int CHECKSUM_SIZE = 6;

    private static uint EncodingConstant(Encoding encoding)
        => encoding == Encoding.Bech32 ? 1u : 0x2bc830a3u;

    /// <summary>
    /// Compute the BCH polymod remainder over GF(32). Input is a sequence of 5-bit values.
    /// Returns a 30-bit value whose 5-bit groups are the remainder coefficients.
    /// </summary>
    public static uint PolyMod(ReadOnlySpan<byte> v)
    {
        uint c = 1; // bit-packed coefficients, starting at 1
        foreach (var vi in v)
        {
            // Take the top 5 bits as c0 (x^5 coefficient), then shift/add the new term.
            uint c0 = c >> 25;
            c = ((c & 0x1ffffffu) << 5) ^ vi;

            // Conditionally add {2^n} * k(x) for each set bit in c0.
            if ((c0 & 1) != 0)  c ^= 0x3b6a57b2u;
            if ((c0 & 2) != 0)  c ^= 0x26508e6du;
            if ((c0 & 4) != 0)  c ^= 0x1ea119fau;
            if ((c0 & 8) != 0)  c ^= 0x3d4233ddu;
            if ((c0 & 16) != 0) c ^= 0x2a1462b3u;
        }
        return c;
    }

    /// <summary>
    /// Expand the HRP and append payload values to form the polynomial coefficients
    /// used by PolyMod. This is (hrp high bits) + 0 + (hrp low bits) + data.
    /// </summary>
    public static byte[] PreparePolynomialCoefficients(string hrp, ReadOnlySpan<byte> values)
    {
        // Reserve: hrp + 1 + hrp + data + checksum (caller may append checksum later)
        var ret = new List<byte>(hrp.Length + 1 + hrp.Length + values.Length + CHECKSUM_SIZE);

        // High bits of HRP
        for (int i = 0; i < hrp.Length; i++)
            ret.Add((byte)((byte)hrp[i] >> 5));

        // Separator between high/low parts
        ret.Add(0);

        // Low bits of HRP
        for (int i = 0; i < hrp.Length; i++)
            ret.Add((byte)((byte)hrp[i] & 0x1f));

        // Data payload
        for (int i = 0; i < values.Length; i++)
            ret.Add(values[i]);

        return CollectionsMarshal.AsSpan(ret).ToArray(); // compact to array
    }

    /// <summary>
    /// Create the 6-symbol checksum for (encoding, hrp, data).
    /// Returns six 5-bit values (0..31).
    /// </summary>
    public static byte[] CreateChecksum(Encoding encoding, string hrp, ReadOnlySpan<byte> values)
    {
        var enc = PreparePolynomialCoefficients(hrp, values);

        // Append 6 zero 5-bit groups, then compute remainder and xor with the encoding constant.
        Array.Resize(ref enc, enc.Length + CHECKSUM_SIZE);
        uint mod = PolyMod(enc) ^ EncodingConstant(encoding);

        // Extract 6×5-bit groups from the 30-bit 'mod', highest group first.
        var ret = new byte[CHECKSUM_SIZE];
        for (int i = 0; i < CHECKSUM_SIZE; i++)
            ret[i] = (byte)((mod >> (5 * (5 - i))) & 31u);

        return ret;
    }
}
