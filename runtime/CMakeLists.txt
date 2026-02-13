cmake_minimum_required(VERSION 3.20)
project(cil2cpp VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Runtime library sources
set(RUNTIME_SOURCES
    src/runtime.cpp
    src/gc/gc.cpp
    src/type_system/type_info.cpp
    src/exception/exception.cpp
    src/bcl/System.Object.cpp
    src/bcl/System.String.cpp
    src/bcl/System.Console.cpp
    src/bcl/System.Array.cpp
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
)

# Platform-specific settings
if(WIN32)
    target_compile_definitions(cil2cpp_runtime PRIVATE CIL2CPP_WINDOWS)
    # dbghelp for stack traces — PUBLIC so consumers link it transitively
    target_link_libraries(cil2cpp_runtime PUBLIC $<$<CONFIG:Debug>:dbghelp>)
else()
    target_compile_definitions(cil2cpp_runtime PRIVATE CIL2CPP_POSIX)
    find_package(Threads REQUIRED)
    target_link_libraries(cil2cpp_runtime PUBLIC Threads::Threads)
endif()

# Compiler warnings
if(MSVC)
    target_compile_options(cil2cpp_runtime PRIVATE /W4)
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
