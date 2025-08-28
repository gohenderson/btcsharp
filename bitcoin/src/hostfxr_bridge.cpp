// hostfxr bridge that mirrors your hostclr-hello pattern:
// - only depends on nethost.h + libnethost
// - dlopen hostfxr via get_hostfxr_path
// - forward-declares hostfxr/coreclr delegate typedefs to avoid extra include dirs

#include "hostfxr_bridge.h"

#include <nethost.h>     // get_hostfxr_path
#include <dlfcn.h>       // dlopen/dlsym
#include <cstdio>
#include <cstring>
#include <mutex>
#include <atomic>
#include <string>
#include <unistd.h>
#include <libgen.h>
#include <coreclr_delegates.h>
#include <limits.h>

// --- Forward declarations for hostfxr/CoreCLR delegates (match headers) ---
using hostfxr_handle = void*;

enum hostfxr_delegate_type
{
    hdt_com_activation,
    hdt_load_in_memory_assembly,
    hdt_winrt_activation,
    hdt_com_register,
    hdt_com_unregister,
    hdt_load_assembly_and_get_function_pointer,
    hdt_get_function_pointer
};

using hostfxr_initialize_for_runtime_config_fn =
    int(*)(const char* runtime_config_path, void* parameters, /*out*/ hostfxr_handle* host_context_handle);

using hostfxr_get_runtime_delegate_fn =
    int(*)(hostfxr_handle host_context_handle, hostfxr_delegate_type type, /*out*/ void** delegate);

using hostfxr_close_fn =
    int(*)(hostfxr_handle host_context_handle);

// Signature of the load_assembly_and_get_function_pointer delegate:
using load_assembly_and_get_function_pointer_fn =
    int(*)(const char* assembly_path,
           const char* type_name,                     // "Namespace.Type, Assembly"
           const char* method_name,                   // "Method"
           const char* delegate_type_name,            // "UNMANAGEDCALLERSONLY_METHOD"
           void* reserved,
           /*out*/ void** delegate);

// --- Tiny struct to hold hostfxr exports ---
struct hostfxr_exports {
    hostfxr_initialize_for_runtime_config_fn initialize = nullptr;
    hostfxr_get_runtime_delegate_fn           get_delegate = nullptr;
    hostfxr_close_fn                          close = nullptr;
    void*                                     lib_handle = nullptr;
};

static bool load_hostfxr_exports(hostfxr_exports& out, const char* runtimeconfig_path)
{
    char hostfxr_path[1024];
    size_t size = sizeof(hostfxr_path);

    get_hostfxr_parameters params{};
    params.size = sizeof(params);
    params.assembly_path = runtimeconfig_path;

    int rc = get_hostfxr_path(hostfxr_path, &size, &params);
    if (rc != 0) {
        std::fprintf(stderr, "[fxr_bridge] get_hostfxr_path failed rc=%d\n", rc);
        return false;
    }

    void* lib = dlopen(hostfxr_path, RTLD_LAZY | RTLD_GLOBAL);
    if (!lib) {
        std::fprintf(stderr, "[fxr_bridge] dlopen(%s) failed: %s\n", hostfxr_path, dlerror());
        return false;
    }

    out.initialize  = (hostfxr_initialize_for_runtime_config_fn)dlsym(lib, "hostfxr_initialize_for_runtime_config");
    out.get_delegate= (hostfxr_get_runtime_delegate_fn)         dlsym(lib, "hostfxr_get_runtime_delegate");
    out.close       = (hostfxr_close_fn)                        dlsym(lib, "hostfxr_close");

    if (!out.initialize || !out.get_delegate || !out.close) {
        std::fprintf(stderr, "[fxr_bridge] missing hostfxr exports\n");
        dlclose(lib);
        std::memset(&out, 0, sizeof(out));
        return false;
    }

    out.lib_handle = lib;
    return true;
}

// --- Globals (per-process) ---
static std::mutex g_lock;
static std::atomic<int> g_refcount{0};
static hostfxr_exports g_fxr{};
static hostfxr_handle g_ctx = nullptr;
static load_assembly_and_get_function_pointer_fn g_load_asm_and_get_ptr = nullptr;

static std::string dir_of_self()
{
    char buf[PATH_MAX];
    ssize_t n = readlink("/proc/self/exe", buf, sizeof(buf)-1);
    if (n <= 0) return ".";
    buf[n] = '\0';
    return std::string(dirname(buf));
}

bool fxr_init_next_to_exe(const char* runtimeconfig_file_name)
{
    std::string rcfg = dir_of_self();
    rcfg += "/";
    rcfg += runtimeconfig_file_name ? runtimeconfig_file_name : "";
    return fxr_init_from_runtimeconfig(rcfg.c_str());
}

bool fxr_init_from_runtimeconfig(const char* runtimeconfig_path)
{
    if (!runtimeconfig_path || !*runtimeconfig_path) {
        std::fprintf(stderr, "[fxr_bridge] runtimeconfig_path is null/empty\n");
        return false;
    }

    if (g_refcount.load(std::memory_order_acquire) > 0) {
        g_refcount.fetch_add(1, std::memory_order_acq_rel);
        return true;
    }

    std::lock_guard<std::mutex> _g(g_lock);
    if (g_refcount.load(std::memory_order_relaxed) > 0) {
        g_refcount.fetch_add(1, std::memory_order_relaxed);
        return true;
    }

    if (!load_hostfxr_exports(g_fxr, runtimeconfig_path)) {
        return false;
    }

    int rc = g_fxr.initialize(runtimeconfig_path, nullptr, &g_ctx);
    if (rc != 0 || !g_ctx) {
        std::fprintf(stderr, "[fxr_bridge] hostfxr_initialize_for_runtime_config failed rc=%d\n", rc);
        return false;
    }

    void* tmp = nullptr;
    rc = g_fxr.get_delegate(g_ctx, hdt_load_assembly_and_get_function_pointer, &tmp);
    if (rc != 0 || !tmp) {
        std::fprintf(stderr, "[fxr_bridge] hostfxr_get_runtime_delegate failed rc=%d\n", rc);
        g_fxr.close(g_ctx);
        g_ctx = nullptr;
        return false;
    }

    g_load_asm_and_get_ptr = (load_assembly_and_get_function_pointer_fn)tmp;
    g_refcount.store(1, std::memory_order_release);
    return true;
}

bool fxr_load_umco(const char* assembly_path,
                   const char* type_qual_name,
                   const char* method_name,
                   void** out_fnptr)
{
    if (!g_ctx || !g_load_asm_and_get_ptr) {
        std::fprintf(stderr, "[fxr_bridge] load requested before init\n");
        return false;
    }
    if (!assembly_path || !type_qual_name || !method_name || !out_fnptr) {
        std::fprintf(stderr, "[fxr_bridge] invalid args to fxr_load_umco\n");
        return false;
    }

    *out_fnptr = nullptr;

    int rc = g_load_asm_and_get_ptr(
        assembly_path,
        type_qual_name,
        method_name,
        UNMANAGEDCALLERSONLY_METHOD,
        nullptr,
        out_fnptr
    );

    if (rc != 0 || *out_fnptr == nullptr) {
        std::fprintf(stderr, "[fxr_bridge] load_assembly_and_get_function_pointer failed rc=%d\n", rc);
        return false;
    }
    return true;
}

void fxr_shutdown()
{
    int old = g_refcount.load(std::memory_order_acquire);
    if (old <= 0) return;

    if (g_refcount.fetch_sub(1, std::memory_order_acq_rel) != 1) {
        return; // others still using it
    }

    std::lock_guard<std::mutex> _g(g_lock);
    if (g_ctx) {
        g_fxr.close(g_ctx);
        g_ctx = nullptr;
        g_load_asm_and_get_ptr = nullptr;
    }
    if (g_fxr.lib_handle) {
        dlclose(g_fxr.lib_handle);
        std::memset(&g_fxr, 0, sizeof(g_fxr));
    }
}
