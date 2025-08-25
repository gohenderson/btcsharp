#pragma once
#include <stdbool.h>

#ifdef __cplusplus
extern "C" {
#endif

    // Initialize CLR from a runtimeconfig.json (ref-counted, safe to call multiple times).
    bool fxr_init_from_runtimeconfig(const char* runtimeconfig_path);

    // Load an UnmanagedCallersOnly method pointer.
    // type_qual_name: "Namespace.TypeName, AssemblyName"
    // method_name:    "MethodName"
    // On success, *out_fnptr is set and returns true.
    bool fxr_load_umco(const char* assembly_path,
                       const char* type_qual_name,
                       const char* method_name,
                       void** out_fnptr);

    // Decrement refcount; closes CLR when it hits zero.
    void fxr_shutdown();

    // Convenience: init using a runtimeconfig that sits next to /proc/self/exe.
    bool fxr_init_next_to_exe(const char* runtimeconfig_file_name);

#ifdef __cplusplus
}
#endif
