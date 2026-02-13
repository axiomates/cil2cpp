cmake_minimum_required(VERSION 3.20)
project(cil2cpp_tests CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# Fetch Google Test
include(FetchContent)
FetchContent_Declare(
    googletest
    GIT_REPOSITORY https://github.com/google/googletest.git
    GIT_TAG v1.15.2
)
# Prevent overriding the parent project's compiler/linker settings on Windows
set(gtest_force_shared_crt ON CACHE BOOL "" FORCE)
FetchContent_MakeAvailable(googletest)

enable_testing()

# Build the runtime library from parent directory
# We include the parent CMakeLists via add_subdirectory to reuse the target
add_subdirectory(${CMAKE_CURRENT_SOURCE_DIR}/.. ${CMAKE_CURRENT_BINARY_DIR}/runtime_build)

# Test executable
add_executable(cil2cpp_tests
    test_gc.cpp
    test_string.cpp
    test_array.cpp
    test_type_system.cpp
    test_object.cpp
    test_exception.cpp
)

target_link_libraries(cil2cpp_tests
    PRIVATE
        cil2cpp_runtime
        GTest::gtest_main
)

# Ensure debug defines are set for tests
target_compile_definitions(cil2cpp_tests PRIVATE CIL2CPP_DEBUG)

include(GoogleTest)
gtest_discover_tests(cil2cpp_tests)

# === Coverage option ===
option(ENABLE_COVERAGE "Enable code coverage (GCC/Clang only)" OFF)

if(ENABLE_COVERAGE AND NOT MSVC)
    target_compile_options(cil2cpp_runtime PRIVATE --coverage -fprofile-arcs -ftest-coverage)
    target_link_options(cil2cpp_runtime PRIVATE --coverage)
    target_compile_options(cil2cpp_tests PRIVATE --coverage -fprofile-arcs -ftest-coverage)
    target_link_options(cil2cpp_tests PRIVATE --coverage)

    # Custom target to generate coverage report
    find_program(LCOV lcov)
    find_program(GENHTML genhtml)

    if(LCOV AND GENHTML)
        add_custom_target(coverage
            COMMAND ${LCOV} --capture --directory . --output-file coverage.info --ignore-errors mismatch
            COMMAND ${LCOV} --remove coverage.info "/usr/*" "*/googletest/*" "*/tests/*" --output-file coverage_filtered.info
            COMMAND ${GENHTML} coverage_filtered.info --output-directory coverage_report
            WORKING_DIRECTORY ${CMAKE_BINARY_DIR}
            DEPENDS cil2cpp_tests
            COMMENT "Generating code coverage report..."
        )
    endif()
endif()
