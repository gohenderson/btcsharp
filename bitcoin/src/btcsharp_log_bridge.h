// src/btcsharp_log_bridge.h
#pragma once
#include <cstddef>
#include <cstdint>

#ifdef __cplusplus
extern "C" {
#endif

    // levels mirrored with managed enum
    enum btcsharp_log_level { TRACE=0, DEBUG_L=1, INFO=2, WARN=3, ERROR_L=4, FATAL=5 };

    bool btcsharp_logging_init(const char* managed_assembly_path, const char* typeName, const char* methodName);
    void btcsharp_log(enum btcsharp_log_level lvl, const char* msg, size_t len);
    void btcsharp_logging_shutdown();

#ifdef __cplusplus
}
#endif