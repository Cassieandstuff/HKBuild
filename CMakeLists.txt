# ---------------------------------------------------------------------------
# HKBuild — C# .NET 9 behavior compiler
#
# Publishes HKBuild as a single-file self-contained win-x64 exe into
# ${CMAKE_BINARY_DIR}/hkbuild_sc/.  Single-file bundles the entire .NET
# runtime into one executable; native DLLs are extracted at first run to
# %LOCALAPPDATA%\.net\ (outside Data\, so MO2's USVFS never redirects them).
#
# This is intentionally single-file so that BehaviorGenerator copies only
# one ~70 MB file to %TEMP%\BSB_hkbuild\ at runtime, sidestepping USVFS
# interference with .NET startup DLL loads from Data\.
#
# Sets the HKBUILD_EXE cache variable consumed by behavior_src targets.
# ---------------------------------------------------------------------------

set(HKBUILD_CSPROJ "${CMAKE_CURRENT_SOURCE_DIR}/src/HKBuild.csproj")

# Self-contained publish output — kept separate from the cmake subdir dir
# (${CMAKE_BINARY_DIR}/hkbuild/) so cmake bookkeeping files don't mix with
# the published DLLs that we copy into the mod folder.
set(_hkbuild_sc_dir "${CMAKE_BINARY_DIR}/hkbuild_sc")

set(HKBUILD_EXE "${_hkbuild_sc_dir}/HKBuild${CMAKE_EXECUTABLE_SUFFIX}"
    CACHE FILEPATH "Path to the built HKBuild executable" FORCE)

# Track C# sources so the exe is rebuilt when they change.
file(GLOB_RECURSE _hkbuild_sources
    "${CMAKE_CURRENT_SOURCE_DIR}/src/*.cs"
    "${HKBUILD_CSPROJ}"
)

add_custom_command(
    OUTPUT  "${HKBUILD_EXE}"
    COMMAND dotnet publish "${HKBUILD_CSPROJ}"
            -c Release
            -r win-x64
            --self-contained true
            -p:PublishSingleFile=true
            -o "${_hkbuild_sc_dir}"
    DEPENDS ${_hkbuild_sources}
    WORKING_DIRECTORY "${CMAKE_CURRENT_SOURCE_DIR}/src"
    COMMENT "Building HKBuild (single-file self-contained -> hkbuild_sc/HKBuild.exe)"
    VERBATIM
)

add_custom_target(HKBuild
    DEPENDS "${HKBUILD_EXE}"
)
