cmake_minimum_required(VERSION 3.22)
project(BtcSharpSuperbuild LANGUAGES C CXX)

# Put all artifacts in <repo>/build
set(OUTPUT_DIR "${CMAKE_SOURCE_DIR}/build")
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${OUTPUT_DIR}")
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY "${OUTPUT_DIR}")
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY "${OUTPUT_DIR}")
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)
foreach(cfg IN ITEMS Debug Release RelWithDebInfo MinSizeRel)
    string(TOUPPER "${cfg}" CFGU)
    set(CMAKE_RUNTIME_OUTPUT_DIRECTORY_${CFGU} "${OUTPUT_DIR}")
    set(CMAKE_LIBRARY_OUTPUT_DIRECTORY_${CFGU} "${OUTPUT_DIR}")
    set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY_${CFGU} "${OUTPUT_DIR}")
endforeach()

# ---- C++ side ----
add_subdirectory(bitcoin)

# ---- .NET side (btcsharp) ----
find_program(DOTNET_CMD dotnet REQUIRED)

set(DOTNET_CONFIGURATION "Release")
set(DOTNET_OUTPUT_DIR "${CMAKE_SOURCE_DIR}/build")
set(BTCSHARP_CSPROJ "${CMAKE_SOURCE_DIR}/btcsharp/btcsharp/btcsharp.csproj")
set(BTCSHARP_PROJ_DIR "${CMAKE_SOURCE_DIR}/btcsharp/btcsharp")

add_custom_target(btcsharp_publish ALL
        COMMAND "${DOTNET_CMD}" publish
        "${BTCSHARP_CSPROJ}"
        -c "${DOTNET_CONFIGURATION}"
        -o "${DOTNET_OUTPUT_DIR}"
        -p:UseAppHost=false
        WORKING_DIRECTORY "${BTCSHARP_PROJ_DIR}"
        COMMENT "Publishing btcsharp to ${DOTNET_OUTPUT_DIR}"
        VERBATIM
)

# ---- Aggregate everything (define ONCE) ----
add_custom_target(all_build COMMENT "Build everything (C++ + .NET)")

# Always build .NET as part of all_build
add_dependencies(all_build btcsharp_publish)

# Add native deps if present (bitcoind/etc may vary by your bitcoin CMake)
foreach(t IN ITEMS bitcoind bitcoin-cli bitcoin-tx bitcoin-wallet test_bitcoin)
    if(TARGET ${t})
        add_dependencies(all_build ${t})
    endif()
endforeach()
