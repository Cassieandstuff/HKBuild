# ---------------------------------------------------------------------------
# HKBuild — Havok Behavior Compiler & Converter
# C# .NET 9 tool. Publishes as a single-file self-contained win-x64 exe.
#
# Standalone:  cmake -B build -G "Visual Studio 17 2022" && cmake --build build
# Workspace:   add_subdirectory(HKBuild) from parent CMakeLists.txt
#
# After build, HKBUILD_EXE cache variable points to the published executable.
# Downstream targets that need hkbuild should:
#   add_dependencies(my_target HKBuild)
#   target_compile_definitions(my_target PRIVATE
#       HKBUILD_EXE="$<TARGET_PROPERTY:HKBuild,HKBUILD_EXE>")
# or simply use the HKBUILD_EXE cache variable at configure time.
# ---------------------------------------------------------------------------

cmake_minimum_required(VERSION 3.25)

# Only declare a project when we are the root (standalone build).
# When included via add_subdirectory the parent project is already active.
if(CMAKE_SOURCE_DIR STREQUAL CMAKE_CURRENT_SOURCE_DIR)
    project(HKBuild LANGUAGES NONE)
    message(STATUS "HKBuild: standalone build")
else()
    message(STATUS "HKBuild: workspace build (parent: ${CMAKE_PROJECT_NAME})")
endif()

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------
set(HKBUILD_CSPROJ "${CMAKE_CURRENT_SOURCE_DIR}/src/HKBuild.csproj")

# Publish to a dedicated directory so CMake bookkeeping files don't mix with
# the published output (important when copying to %TEMP% or Data\ at runtime).
set(_hkbuild_out_dir "${CMAKE_BINARY_DIR}/hkbuild_sc")

set(HKBUILD_EXE "${_hkbuild_out_dir}/HKBuild${CMAKE_EXECUTABLE_SUFFIX}"
    CACHE FILEPATH "Path to the built HKBuild executable" FORCE)

# ---------------------------------------------------------------------------
# Source tracking — rebuilds when any .cs file or the csproj changes
# ---------------------------------------------------------------------------
file(GLOB_RECURSE _hkbuild_sources
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cs"
    "${HKBUILD_CSPROJ}"
)

# ---------------------------------------------------------------------------
# Build step: dotnet publish → single-file self-contained win-x64 exe
#
# Single-file bundles the .NET runtime into one ~70 MB executable.
# Native DLLs are extracted at first run to %LOCALAPPDATA%\.net\, which is
# outside Data\ so MO2's USVFS virtual filesystem never intercepts them.
# ---------------------------------------------------------------------------
add_custom_command(
    OUTPUT  "${HKBUILD_EXE}"
    COMMAND dotnet publish "${HKBUILD_CSPROJ}"
            -c Release
            -r win-x64
            --self-contained true
            -p:PublishSingleFile=true
            -o "${_hkbuild_out_dir}"
    DEPENDS ${_hkbuild_sources}
    WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/src"
    COMMENT "Building HKBuild (single-file self-contained → hkbuild_sc/HKBuild.exe)"
    VERBATIM
)

add_custom_target(HKBuild ALL
    DEPENDS "${HKBUILD_EXE}"
)

# Expose the exe path as a target property so workspace consumers can query it
set_target_properties(HKBuild PROPERTIES
    HKBUILD_EXE "${HKBUILD_EXE}"
)
