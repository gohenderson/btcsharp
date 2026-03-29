// src/btcsharp_bech32_bridge.cpp
#include "btcsharp_bech32_bridge.h"
#include "hostfxr_bridge.h"

#include <atomic>
#include <cstdio>
#include <cstring>
#include <libgen.h>
#include <string>

#ifndef _MSC_VER
#define __cdecl
#endif

// ---------------------------------------------------------------------------
// Managed function pointer types  (must match [UnmanagedCallersOnly] sigs)
// ---------------------------------------------------------------------------

using managed_bech32_encode_fn = void(__cdecl*)(
    int32_t         encoding,
    const uint8_t*  hrp,        int32_t hrp_len,
    const uint8_t*  values,     int32_t values_len,
    uint8_t*        out_buf,    int32_t out_cap,
    int32_t*        out_len);

using managed_bech32_decode_fn = void(__cdecl*)(
    const uint8_t*           str,
    int32_t                  str_len,
    int32_t                  limit,
    btcsharp_bech32_result*  result);

static std::atomic<managed_bech32_encode_fn> s_encode{nullptr};
static std::atomic<managed_bech32_decode_fn> s_decode{nullptr};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

static std::string dir_of_self()
{
    char buf[PATH_MAX];
    ssize_t n = readlink("/proc/self/exe", buf, sizeof(buf) - 1);
    if (n <= 0) return ".";
    buf[n] = '\0';
    return std::string(dirname(buf));
}

static bool load_fn(const char* asm_path, const char* method, void** out)
{
    return fxr_load_umco(
        asm_path,
        "BtcSharp.Interop.Bech32Bridge, btcsharp",
        method,
        out);
}

// ---------------------------------------------------------------------------
// Lifecycle
// ---------------------------------------------------------------------------

bool btcsharp_bech32_init()
{
    const std::string dir      = dir_of_self();
    const std::string asm_path = dir + "/btcsharp.dll";
    const std::string rcfg     = dir + "/btcsharp.runtimeconfig.json";

    if (!fxr_init_from_runtimeconfig(rcfg.c_str())) {
        std::fprintf(stderr, "[btcsharp_bech32] fxr init failed\n");
        return false;
    }

    void* enc_ptr = nullptr;
    if (!load_fn(asm_path.c_str(), "Encode", &enc_ptr)) {
        std::fprintf(stderr, "[btcsharp_bech32] failed to load Encode\n");
        return false;
    }
    s_encode.store(reinterpret_cast<managed_bech32_encode_fn>(enc_ptr),
                   std::memory_order_release);

    void* dec_ptr = nullptr;
    if (!load_fn(asm_path.c_str(), "Decode", &dec_ptr)) {
        std::fprintf(stderr, "[btcsharp_bech32] failed to load Decode\n");
        return false;
    }
    s_decode.store(reinterpret_cast<managed_bech32_decode_fn>(dec_ptr),
                   std::memory_order_release);

    return true;
}

void btcsharp_bech32_shutdown()
{
    s_encode.store(nullptr, std::memory_order_release);
    s_decode.store(nullptr, std::memory_order_release);
    fxr_shutdown();
}

// ---------------------------------------------------------------------------
// Raw C-ABI entry points
// ---------------------------------------------------------------------------

void btcsharp_bech32_encode(
    int32_t        encoding,
    const char*    hrp,       int32_t hrp_len,
    const uint8_t* values,    int32_t values_len,
    char*          out_buf,   int32_t out_cap,
    int32_t*       out_len)
{
    if (out_len) *out_len = 0;

    auto fn = s_encode.load(std::memory_order_acquire);
    if (!fn) return;

    fn(encoding,
       reinterpret_cast<const uint8_t*>(hrp), hrp_len,
       values, values_len,
       reinterpret_cast<uint8_t*>(out_buf), out_cap,
       out_len);
}

void btcsharp_bech32_decode(
    const char*             str,
    int32_t                 str_len,
    int32_t                 limit,
    btcsharp_bech32_result* result)
{
    if (!result) return;
    std::memset(result, 0, sizeof(*result));

    auto fn = s_decode.load(std::memory_order_acquire);
    if (!fn) return;

    fn(reinterpret_cast<const uint8_t*>(str), str_len, limit, result);
}

// ---------------------------------------------------------------------------
// Thin C++ wrappers — restore original bech32:: API signature
// ---------------------------------------------------------------------------

std::string bech32_managed::Encode(
    bech32::Encoding             encoding,
    const std::string&           hrp,
    const std::vector<uint8_t>&  values)
{
    auto fn = s_encode.load(std::memory_order_acquire);
    if (!fn)
        return bech32::Encode(encoding, hrp, values); // native fallback

    char out_buf[91] = {};
    int32_t out_len  = 0;

    fn(static_cast<int32_t>(encoding),
       reinterpret_cast<const uint8_t*>(hrp.c_str()), static_cast<int32_t>(hrp.size()),
       values.data(), static_cast<int32_t>(values.size()),
       reinterpret_cast<uint8_t*>(out_buf), static_cast<int32_t>(sizeof(out_buf)),
       &out_len);

    return std::string(out_buf, static_cast<std::string::size_type>(out_len));
}

bech32::DecodeResult bech32_managed::Decode(
    const std::string& str,
    bech32::CharLimit  limit)
{
    auto fn = s_decode.load(std::memory_order_acquire);
    if (!fn)
        return bech32::Decode(str, limit); // native fallback

    btcsharp_bech32_result r{};
    fn(reinterpret_cast<const uint8_t*>(str.c_str()), static_cast<int32_t>(str.size()),
       static_cast<int32_t>(limit),
       &r);

    auto enc = static_cast<bech32::Encoding>(r.encoding);
    if (enc == bech32::Encoding::INVALID) return {};

    return {enc,
            std::string(r.hrp, static_cast<std::string::size_type>(r.hrp_len)),
            std::vector<uint8_t>(r.data, r.data + r.data_len)};
}
