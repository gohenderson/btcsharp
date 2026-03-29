// Copyright (c) 2017 Pieter Wuille
// Copyright (c) 2021-2022 The Bitcoin Core developers
// Distributed under the MIT software license, see the accompanying
// file COPYING or http://www.opensource.org/licenses/mit-license.php.
//
// C# port of bitcoin/src/test/bech32_tests.cpp

using BtcSharp;
using Xunit;

namespace BtcSharp.Tests;

public class Bech32Tests
{
    // -------------------------------------------------------------------------
    // bech32_testvectors_valid
    //
    // For each valid Bech32 string: decode, verify encoding is BECH32, re-encode
    // and compare case-insensitively with the original.
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData("A12UEL5L")]
    [InlineData("a12uel5l")]
    [InlineData("an83characterlonghumanreadablepartthatcontainsthenumber1andtheexcludedcharactersbio1tt5tgs")]
    [InlineData("abcdef1qpzry9x8gf2tvdw0s3jn54khce6mua7lmqqqxw")]
    [InlineData("11qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqc8247j")]
    [InlineData("split1checkupstagehandshakeupstreamerranterredcaperred2y9e3w")]
    [InlineData("?1ezyfcl")]
    public void Bech32_ValidVectors_DecodeAndReencode(string str)
    {
        var dec = Bech32.Decode(str);
        Assert.Equal(Encoding.Bech32, dec.Encoding);

        string recode = Bech32.Encode(Encoding.Bech32, dec.Hrp, dec.Data);
        Assert.NotEmpty(recode);
        Assert.Equal(str, recode, StringComparer.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // bech32m_testvectors_valid
    // -------------------------------------------------------------------------
    [Theory]
    [InlineData("A1LQFN3A")]
    [InlineData("a1lqfn3a")]
    [InlineData("an83characterlonghumanreadablepartthatcontainsthetheexcludedcharactersbioandnumber11sg7hg6")]
    [InlineData("abcdef1l7aum6echk45nj3s0wdvt2fg8x9yrzpqzd3ryx")]
    [InlineData("11llllllllllllllllllllllllllllllllllllllllllllllllllllllllllllllllllllllllllllllllllludsr8")]
    [InlineData("split1checkupstagehandshakeupstreamerranterredcaperredlc445v")]
    [InlineData("?1v759aa")]
    public void Bech32m_ValidVectors_DecodeAndReencode(string str)
    {
        var dec = Bech32.Decode(str);
        Assert.Equal(Encoding.Bech32m, dec.Encoding);

        string recode = Bech32.Encode(Encoding.Bech32m, dec.Hrp, dec.Data);
        Assert.NotEmpty(recode);
        Assert.Equal(str, recode, StringComparer.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // bech32_testvectors_invalid
    //
    // For each invalid string: Decode returns Invalid, and LocateErrors returns
    // the expected error message and error positions.
    // -------------------------------------------------------------------------
    public static IEnumerable<object[]> Bech32InvalidCases => new[]
    {
        // str                                                       error message                         positions
        new object[] { " 1nwldj5",                                  "Invalid character or mixed case",    new[] {0}   },
        new object[] { "\x7f1axkwrx",                               "Invalid character or mixed case",    new[] {0}   },
        new object[] { "\x801eym55h",                               "Invalid character or mixed case",    new[] {0}   },
        new object[] { "an84characterslonghumanreadablepartthatcontainsthenumber1andtheexcludedcharactersbio1569pvx",
                                                                     "Bech32 string too long",             new[] {90}  },
        new object[] { "pzry9x0s0muk",                              "Missing separator",                  Array.Empty<int>() },
        new object[] { "1pzry9x0s0muk",                             "Invalid separator position",         new[] {0}   },
        new object[] { "x1b4n0q5v",                                 "Invalid Base 32 character",          new[] {2}   },
        new object[] { "li1dgmt3",                                   "Invalid separator position",         new[] {2}   },
        new object[] { "de1lg7wt\xff",                              "Invalid character or mixed case",    new[] {8}   },
        // The checksum is calculated from the uppercase form, so the entire string is invalid.
        new object[] { "A1G7SGD8",                                   "Invalid checksum",                   Array.Empty<int>() },
        new object[] { "10a06t8",                                    "Invalid separator position",         new[] {0}   },
        new object[] { "1qzzfhee",                                   "Invalid separator position",         new[] {0}   },
        new object[] { "a12UEL5L",                                   "Invalid character or mixed case",    new[] {3,4,5,7} },
        new object[] { "A12uEL5L",                                   "Invalid character or mixed case",    new[] {3}   },
        new object[] { "abcdef1qpzrz9x8gf2tvdw0s3jn54khce6mua7lmqqqxw",
                                                                     "Invalid Bech32 checksum",            new[] {11}  },
        new object[] { "test1zg69w7y6hn0aqy352euf40x77qddq3dc",
                                                                     "Invalid Bech32 checksum",            new[] {9,16} },
    };

    [Theory]
    [MemberData(nameof(Bech32InvalidCases))]
    public void Bech32_InvalidVectors_DecodeReturnsInvalidAndLocatesErrors(
        string str, string expectedError, int[] expectedPositions)
    {
        var dec = Bech32.Decode(str);
        Assert.Equal(Encoding.Invalid, dec.Encoding);

        var (error, locations) = Bech32.LocateErrors(str);
        Assert.Equal(expectedError, error);
        Assert.Equal(expectedPositions, locations);
    }

    // -------------------------------------------------------------------------
    // bech32m_testvectors_invalid
    // -------------------------------------------------------------------------
    public static IEnumerable<object[]> Bech32mInvalidCases => new[]
    {
        new object[] { " 1xj0phk",                                   "Invalid character or mixed case",    new[] {0}   },
        new object[] { "\x7f1g6xzxy",                                "Invalid character or mixed case",    new[] {0}   },
        new object[] { "\x801vctc34",                                "Invalid character or mixed case",    new[] {0}   },
        new object[] { "an84characterslonghumanreadablepartthatcontainsthetheexcludedcharactersbioandnumber11d6pts4",
                                                                      "Bech32 string too long",             new[] {90}  },
        new object[] { "qyrz8wqd2c9m",                               "Missing separator",                  Array.Empty<int>() },
        new object[] { "1qyrz8wqd2c9m",                              "Invalid separator position",         new[] {0}   },
        new object[] { "y1b0jsk6g",                                   "Invalid Base 32 character",          new[] {2}   },
        new object[] { "lt1igcx5c0",                                  "Invalid Base 32 character",          new[] {3}   },
        new object[] { "in1muywd",                                    "Invalid separator position",         new[] {2}   },
        new object[] { "mm1crxm3i",                                   "Invalid Base 32 character",          new[] {8}   },
        new object[] { "au1s5cgom",                                   "Invalid Base 32 character",          new[] {7}   },
        new object[] { "M1VUXWEZ",                                    "Invalid checksum",                   Array.Empty<int>() },
        new object[] { "16plkw9",                                     "Invalid separator position",         new[] {0}   },
        new object[] { "1p2gdwpf",                                    "Invalid separator position",         new[] {0}   },
        new object[] { "abcdef1l7aum6echk45nj2s0wdvt2fg8x9yrzpqzd3ryx",
                                                                      "Invalid Bech32m checksum",           new[] {21}  },
        new object[] { "test1zg69v7y60n00qy352euf40x77qcusag6",
                                                                      "Invalid Bech32m checksum",           new[] {13,32} },
    };

    [Theory]
    [MemberData(nameof(Bech32mInvalidCases))]
    public void Bech32m_InvalidVectors_DecodeReturnsInvalidAndLocatesErrors(
        string str, string expectedError, int[] expectedPositions)
    {
        var dec = Bech32.Decode(str);
        Assert.Equal(Encoding.Invalid, dec.Encoding);

        var (error, locations) = Bech32.LocateErrors(str);
        Assert.Equal(expectedError, error);
        Assert.Equal(expectedPositions, locations);
    }
}
