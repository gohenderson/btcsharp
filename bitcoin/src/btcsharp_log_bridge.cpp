#include "btcsharp_log_bridge.h"
#include "your_hostfxr_bootstrap.h" // whatever you already use to load runtime/get fn ptr
#include <atomic>
#include <cstdio>

// Signature: void(int level, byte* utf8, int len)
using managed_log_fn = void(__cdecl*)(int, const uint8_t*, int);
using managed_shutdown_fn = void(__cdecl*)();

static std::atomic<managed_log_fn> s_log{nullptr};
static std::atomic<managed_shutdown_fn> s_shutdown{nullptr};

bool btcsharp_logging_init(const char* managed_assembly_path,
                           const char* /*typeName*/,
                           const char* /*methodName*/)
{
    // You already have hostfxr wired. Use it to get function pointers to the exported methods.
    void* log_ptr = nullptr;
    void* shut_ptr = nullptr;

    if (!load_managed_method(managed_assembly_path, "BtcSharp.Interop.NativeLogBridge", "BtcSharp_Log", /*isUnmanagedCallersOnly*/ true, &log_ptr)) {
        std::fprintf(stderr, "[btcsharp] managed Log not available; using native stderr fallback.\n");
    } else {
        s_log.store(reinterpret_cast<managed_log_fn>(log_ptr), std::memory_order_release);
    }

    if (load_managed_method(managed_assembly_path, "BtcSharp.Interop.NativeLogBridge", "BtcSharp_Log_Shutdown", true, &shut_ptr)) {
        s_shutdown.store(reinterpret_cast<managed_shutdown_fn>(shut_ptr), std::memory_order_release);
    }

    return s_log.load(std::memory_order_acquire) != nullptr;
}

void btcsharp_log(enum btcsharp_log_level lvl, const char* msg, size_t len)
{
    if (!msg || len == 0) return;

    if (auto fp = s_log.load(std::memory_order_acquire)) {
        fp(static_cast<int>(lvl), reinterpret_cast<const uint8_t*>(msg), static_cast<int>(len));
    } else {
        // fallback: write straight to stderr (no allocation, no locale)
        std::fwrite(msg, 1, len, stderr);
        std::fwrite("\n", 1, 1, stderr);
    }
}

void btcsharp_logging_shutdown()
{
    if (auto fp = s_shutdown.load(std::memory_order_acquire)) fp();
}