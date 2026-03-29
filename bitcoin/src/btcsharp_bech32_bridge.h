// src/btcsharp_bech32_bridge.h
// C++ side of the managed Bech32 interop bridge.
//
// Provides:
//   - btcsharp_bech32_result:  the blittable struct shared with C# (Pack=1)
//   - btcsharp_bech32_init / _shutdown: lifecycle
//   - btcsharp_bech32_encode / _decode: raw C-ABI entry points
//   - bech32_managed::Encode / Decode:  thin C++ wrappers restoring the original
//       bech32:: API signature, for future use when callers are switched over.
//
// Layout of btcsharp_bech32_result must match BtcSharpBech32Result in
// BtcSharp/Interop/Bech32Bridge.cs exactly (both use Pack/pragma pack 1).

#pragma once

#include <bech32.h>
#include <cstdint>
#include <string>
#include <vector>

// ---------------------------------------------------------------------------
// Shared result struct  (Pack=1, no padding)
// ---------------------------------------------------------------------------
#pragma pack(push, 1)
struct btcsharp_bech32_result {
    int32_t encoding;   ///< 0=Invalid, 1=Bech32, 2=Bech32m
    int32_t hrp_len;    ///< byte length of hrp[]  (0 when invalid)
    int32_t data_len;   ///< element count of data[] (0 when invalid)
    char    hrp[91];    ///< null-terminated UTF-8 HRP, max 90 chars + '\0'
    uint8_t data[91];   ///< decoded payload bytes
};
#pragma pack(pop)

static_assert(sizeof(btcsharp_bech32_result) == 4+4+4+91+91,
              "btcsharp_bech32_result size mismatch — check Pack=1 on both sides");

#ifdef __cplusplus
extern "C" {
#endif

// ---------------------------------------------------------------------------
// Lifecycle
// ---------------------------------------------------------------------------

/// Load the managed Bech32 bridge.  Safe to call multiple times (ref-counted
/// through the underlying fxr_init).  Returns true on success.
bool btcsharp_bech32_init();
void btcsharp_bech32_shutdown();

// ---------------------------------------------------------------------------
// Raw C-ABI entry points (mirrors [UnmanagedCallersOnly] signatures)
// ---------------------------------------------------------------------------

/// Encode values as Bech32/Bech32m.
/// On success *out_len is set to the number of ASCII bytes written to out_buf
/// (no null terminator).  On failure *out_len is 0.
void btcsharp_bech32_encode(
    int32_t         encoding,
    const char*     hrp,        int32_t hrp_len,
    const uint8_t*  values,     int32_t values_len,
    char*           out_buf,    int32_t out_cap,
    int32_t*        out_len);

/// Decode a Bech32/Bech32m string into *result.
/// result->encoding is set to 0 (Invalid) on any failure.
void btcsharp_bech32_decode(
    const char*              str,
    int32_t                  str_len,
    int32_t                  limit,
    btcsharp_bech32_result*  result);

#ifdef __cplusplus
} // extern "C"

// ---------------------------------------------------------------------------
// Thin C++ wrappers — restore the original bech32:: API signature.
// Drop-in replacements for bech32::Encode / bech32::Decode once callers are
// switched to #include <btcsharp_bech32_bridge.h>.
// Fall back to the native C++ implementation when the managed bridge is not
// available.
// ---------------------------------------------------------------------------
namespace bech32_managed {

std::string Encode(
    bech32::Encoding             encoding,
    const std::string&           hrp,
    const std::vector<uint8_t>&  values);

bech32::DecodeResult Decode(
    const std::string& str,
    bech32::CharLimit  limit = bech32::CharLimit::BECH32);

} // namespace bech32_managed
#endif // __cplusplus
