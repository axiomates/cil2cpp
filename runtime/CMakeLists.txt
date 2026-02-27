cmake_minimum_required(VERSION 3.20)
project(cil2cpp VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# === BoehmGC (FetchContent) ===
# Cache downloads in .deps/ (gitignored) so deleting build/ won't re-download
set(FETCHCONTENT_BASE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/.deps"
    CACHE PATH "FetchContent cache directory")
include(FetchContent)

# HACK: FetchContent_Populate() is deprecated in CMake 3.30+ (CMP0169), but we
# need it for bdwgc/libatomic_ops because a symlink step is required between
# download and add_subdirectory. FetchContent_MakeAvailable cannot be used here
# since it combines both operations into one call.
if(POLICY CMP0169)
    cmake_policy(SET CMP0169 OLD)
endif()

FetchContent_Declare(
    bdwgc
    GIT_REPOSITORY https://github.com/ivmai/bdwgc.git
    GIT_TAG v8.2.12
)
FetchContent_Declare(
    libatomic_ops
    GIT_REPOSITORY https://github.com/ivmai/libatomic_ops.git
    GIT_TAG v7.10.0
)

# Populate sources (download only, don't add_subdirectory yet)
# bdwgc needs libatomic_ops symlinked into its source tree before add_subdirectory
FetchContent_GetProperties(bdwgc)
if(NOT bdwgc_POPULATED)
    FetchContent_Populate(bdwgc)
endif()
FetchContent_GetProperties(libatomic_ops)
if(NOT libatomic_ops_POPULATED)
    FetchContent_Populate(libatomic_ops)
endif()

# MSVC needs libatomic_ops inside bdwgc source tree
if(MSVC AND NOT EXISTS "${bdwgc_SOURCE_DIR}/libatomic_ops")
    file(CREATE_LINK "${libatomic_ops_SOURCE_DIR}" "${bdwgc_SOURCE_DIR}/libatomic_ops"
         COPY_ON_ERROR SYMBOLIC)
endif()

# Configure bdwgc options and add to build
set(enable_cplusplus ON CACHE BOOL "" FORCE)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(build_tests OFF CACHE BOOL "" FORCE)
set(enable_docs OFF CACHE BOOL "" FORCE)
set(install_headers OFF CACHE BOOL "" FORCE)
add_subdirectory(${bdwgc_SOURCE_DIR} ${bdwgc_BINARY_DIR} EXCLUDE_FROM_ALL)

# Match our DEBUG_POSTFIX so Debug installs gcd.lib, Release installs gc.lib
set_target_properties(gc PROPERTIES DEBUG_POSTFIX d)

# === ICU4C 78.2 (prebuilt binaries on Windows, system package on Linux) ===
if(WIN32)
    FetchContent_Declare(
        icu4c
        URL https://github.com/unicode-org/icu/releases/download/release-78.2/icu4c-78.2-Win64-MSVC2022.zip
        DOWNLOAD_EXTRACT_TIMESTAMP TRUE
    )
    # Prebuilt ZIP has no CMakeLists.txt, so MakeAvailable just downloads/extracts
    FetchContent_MakeAvailable(icu4c)

    # Create IMPORTED targets from prebuilt binaries
    add_library(ICU::uc SHARED IMPORTED)
    set_target_properties(ICU::uc PROPERTIES
        IMPORTED_IMPLIB "${icu4c_SOURCE_DIR}/lib64/icuuc.lib"
        IMPORTED_LOCATION "${icu4c_SOURCE_DIR}/bin64/icuuc78.dll"
        INTERFACE_INCLUDE_DIRECTORIES "${icu4c_SOURCE_DIR}/include"
    )
    add_library(ICU::dt SHARED IMPORTED)
    set_target_properties(ICU::dt PROPERTIES
        IMPORTED_IMPLIB "${icu4c_SOURCE_DIR}/lib64/icudt.lib"
        IMPORTED_LOCATION "${icu4c_SOURCE_DIR}/bin64/icudt78.dll"
    )
    add_library(ICU::in SHARED IMPORTED)
    set_target_properties(ICU::in PROPERTIES
        IMPORTED_IMPLIB "${icu4c_SOURCE_DIR}/lib64/icuin.lib"
        IMPORTED_LOCATION "${icu4c_SOURCE_DIR}/bin64/icuin78.dll"
        INTERFACE_INCLUDE_DIRECTORIES "${icu4c_SOURCE_DIR}/include"
    )
else()
    find_package(ICU REQUIRED COMPONENTS uc data i18n)
endif()

# Runtime library sources
set(RUNTIME_SOURCES
    src/runtime.cpp
    src/gc/gc.cpp
    src/gc/gchandle.cpp
    src/type_system/type_info.cpp
    src/exception/exception.cpp
    src/bcl/System.Object.cpp
    src/bcl/System.String.cpp
    src/bcl/System.Array.cpp
    src/bcl/System.MdArray.cpp
    src/bcl/System.Delegate.cpp
    src/icall/icall.cpp
    src/icall/math.cpp
    src/icall/throw_helper.cpp
    src/icall/unicode_utility.cpp
    src/icall/ascii.cpp
    src/async/task.cpp
    src/async/threadpool.cpp
    src/async/cancellation.cpp
    src/async/async_enumerable.cpp
    src/threading/monitor.cpp
    src/threading/interlocked.cpp
    src/threading/thread.cpp
    src/threading/waithandle.cpp
    src/reflection/type.cpp
    src/reflection/memberinfo.cpp
    src/reflection/assembly.cpp
    src/collections/list.cpp
    src/collections/dictionary.cpp
    src/typed_reference.cpp
    src/unicode/icu_bridge.cpp
    src/globalization/globalization.cpp
    src/interop/marshal.cpp
    src/io/file.cpp
    src/io/path.cpp
    src/io/directory.cpp
)

# Create static library
add_library(cil2cpp_runtime STATIC ${RUNTIME_SOURCES})

# Export as cil2cpp::runtime
set_target_properties(cil2cpp_runtime PROPERTIES
    EXPORT_NAME runtime
    DEBUG_POSTFIX d
)

# Include directories (BUILD_INTERFACE for building, INSTALL_INTERFACE for consumers)
target_include_directories(cil2cpp_runtime
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/src
        ${bdwgc_SOURCE_DIR}/include  # BoehmGC headers (gc/gc.h)
)

# BoehmGC — PRIVATE dep, installed separately for consumers
target_link_libraries(cil2cpp_runtime PRIVATE gc)
# GC_NOT_DLL: tell bdwgc headers we link statically (prevents __declspec(dllimport))
# GC_THREADS: enable multi-thread support in BoehmGC (GC_register_my_thread, etc.)
target_compile_definitions(cil2cpp_runtime PRIVATE GC_NOT_DLL GC_THREADS)

# ICU4C — PRIVATE dep for Unicode support (UTF conversion, char classification, globalization)
target_link_libraries(cil2cpp_runtime PRIVATE ICU::uc ICU::dt ICU::in)

# Platform-specific settings
if(WIN32)
    target_compile_definitions(cil2cpp_runtime PRIVATE CIL2CPP_WINDOWS)
    # dbghelp for stack traces — PUBLIC so consumers link it transitively
    target_link_libraries(cil2cpp_runtime PUBLIC $<$<CONFIG:Debug>:dbghelp>)
else()
    target_compile_definitions(cil2cpp_runtime PRIVATE CIL2CPP_POSIX)
endif()

# Threads — PUBLIC so consumers link it transitively (needed for std::thread, Monitor, etc.)
find_package(Threads REQUIRED)
target_link_libraries(cil2cpp_runtime PUBLIC Threads::Threads)

# Compiler warnings
if(MSVC)
    target_compile_options(cil2cpp_runtime PRIVATE /W4 /utf-8)
else()
    target_compile_options(cil2cpp_runtime PRIVATE -Wall -Wextra -Wpedantic)
endif()

# Debug/Release configuration
target_compile_definitions(cil2cpp_runtime PRIVATE
    $<$<CONFIG:Debug>:CIL2CPP_DEBUG>
)

if(MSVC)
    target_compile_options(cil2cpp_runtime PRIVATE
        $<$<CONFIG:Debug>:/Zi /Od /RTC1>
        $<$<CONFIG:Release>:/O2 /DNDEBUG>
    )
else()
    target_compile_options(cil2cpp_runtime PRIVATE
        $<$<CONFIG:Debug>:-g -O0>
        $<$<CONFIG:Release>:-O2 -DNDEBUG>
    )
endif()

# ===== Install =====

install(TARGETS cil2cpp_runtime
    EXPORT cil2cppTargets
    ARCHIVE DESTINATION lib
    LIBRARY DESTINATION lib
)

install(DIRECTORY include/
    DESTINATION include
)

# Install BoehmGC library (needed by consumers at link time)
install(FILES $<TARGET_FILE:gc> DESTINATION lib)

# Install ICU libraries (needed by consumers at link + run time)
if(WIN32)
    install(FILES
        "${icu4c_SOURCE_DIR}/lib64/icuuc.lib"
        "${icu4c_SOURCE_DIR}/lib64/icudt.lib"
        "${icu4c_SOURCE_DIR}/lib64/icuin.lib"
        DESTINATION lib
    )
    install(FILES
        "${icu4c_SOURCE_DIR}/bin64/icuuc78.dll"
        "${icu4c_SOURCE_DIR}/bin64/icudt78.dll"
        "${icu4c_SOURCE_DIR}/bin64/icuin78.dll"
        DESTINATION bin
    )
endif()

# CMake package config
install(EXPORT cil2cppTargets
    FILE cil2cppTargets.cmake
    NAMESPACE cil2cpp::
    DESTINATION lib/cmake/cil2cpp
)

include(CMakePackageConfigHelpers)

configure_package_config_file(
    "${CMAKE_CURRENT_SOURCE_DIR}/cmake/cil2cppConfig.cmake.in"
    "${CMAKE_CURRENT_BINARY_DIR}/cil2cppConfig.cmake"
    INSTALL_DESTINATION lib/cmake/cil2cpp
)

write_basic_package_version_file(
    "${CMAKE_CURRENT_BINARY_DIR}/cil2cppConfigVersion.cmake"
    VERSION ${PROJECT_VERSION}
    COMPATIBILITY AnyNewerVersion
)

install(FILES
    "${CMAKE_CURRENT_BINARY_DIR}/cil2cppConfig.cmake"
    "${CMAKE_CURRENT_BINARY_DIR}/cil2cppConfigVersion.cmake"
    DESTINATION lib/cmake/cil2cpp
)
