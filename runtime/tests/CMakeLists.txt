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
    test_delegate.cpp
    test_boxing.cpp
    test_threading.cpp
    test_reflection.cpp
    test_memberinfo.cpp
    test_collections.cpp
    test_async.cpp
    test_typed_reference.cpp
    test_unicode.cpp
    test_checked.cpp
    test_globalization.cpp
    test_io.cpp
)

target_link_libraries(cil2cpp_tests
    PRIVATE
        cil2cpp_runtime
        GTest::gtest_main
)

# Expose BoehmGC headers for tests that directly call GC APIs (e.g. GC_invoke_finalizers)
# gc_SOURCE_DIR is set by bdwgc's project() command (cache variable)
target_include_directories(cil2cpp_tests PRIVATE "${gc_SOURCE_DIR}/include")

# Copy ICU DLLs to test output directory (Windows only)
if(WIN32 AND TARGET ICU::uc)
    add_custom_command(TARGET cil2cpp_tests POST_BUILD
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<TARGET_PROPERTY:ICU::uc,IMPORTED_LOCATION>
            $<TARGET_FILE_DIR:cil2cpp_tests>
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<TARGET_PROPERTY:ICU::dt,IMPORTED_LOCATION>
            $<TARGET_FILE_DIR:cil2cpp_tests>
        COMMAND ${CMAKE_COMMAND} -E copy_if_different
            $<TARGET_PROPERTY:ICU::in,IMPORTED_LOCATION>
            $<TARGET_FILE_DIR:cil2cpp_tests>
        COMMENT "Copying ICU DLLs to test output directory"
    )
endif()

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
