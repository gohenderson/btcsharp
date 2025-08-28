#include "btcsharp_log_bridge.h"
#include "hostfxr_bridge.h"
#include <atomic>
#include <cstdio>
#include <libgen.h>
#include <string>

#ifndef _MSC_VER
#define __cdecl
#endif

// Managed signature: void(int level, byte* utf8, int len)
using managed_log_fn = void(__cdecl*)(int, const unsigned char*, int);
using managed_shutdown_fn = void(__cdecl*)();

static std::atomic<managed_log_fn>     s_log{nullptr};
static std::atomic<managed_shutdown_fn> s_shutdown{nullptr};

static std::string dir_of_self()
{
    char buf[PATH_MAX];
    ssize_t n = readlink("/proc/self/exe", buf, sizeof(buf)-1);
    if (n <= 0) return ".";
    buf[n] = '\0';
    return std::string(dirname(buf));
}

bool btcsharp_logging_init(const char*, const char*, const char*)
{
    const std::string exe_dir = dir_of_self();
    const std::string asm_path = exe_dir + "/btcsharp.dll";
    const std::string rcfg_path = exe_dir + "/btcsharp.runtimeconfig.json";

    if (!fxr_init_from_runtimeconfig(rcfg_path.c_str())) {
        std::fprintf(stderr, "[btcsharp] fxr init failed\n");
        return false;
    }

    void* log_ptr = nullptr;
    if (!fxr_load_umco(
            asm_path.c_str(),
            "BtcSharp.Interop.NativeLogBridge, btcsharp",
            "Log",
            &log_ptr)) {
        std::fprintf(stderr, "[btcsharp] failed to get Log export\n");
        return false;
            } else {
        s_log.store(reinterpret_cast<managed_log_fn>(log_ptr), std::memory_order_release);
    }

    void* shutdown_ptr = nullptr;
    if (fxr_load_umco(
            asm_path.c_str(),
            "BtcSharp.Interop.NativeLogBridge, btcsharp",
            "Shutdown",
            &shutdown_ptr)) {
        s_shutdown.store(reinterpret_cast<managed_shutdown_fn>(shutdown_ptr), std::memory_order_release);
    }

    return s_log.load(std::memory_order_acquire) != nullptr;
}

void btcsharp_log(enum btcsharp_log_level lvl, const char* msg, size_t len)
{
    if (!msg || len == 0) return;
    if (auto fp = s_log.load(std::memory_order_acquire)) {
        fp(static_cast<int>(lvl), reinterpret_cast<const unsigned char*>(msg), static_cast<int>(len));
    } else {
        // fallback
        std::fwrite(msg, 1, len, stderr);
        std::fwrite("\n", 1, 1, stderr);
    }
}

void btcsharp_logging_shutdown()
{
    if (auto fp = s_shutdown.load(std::memory_order_acquire)) {
        fp();
    }
    fxr_shutdown();
}
