/**
 * CIL2CPP Runtime - System.Console Implementation
 */

#include <cil2cpp/bcl/System.Console.h>
#include <cil2cpp/bcl/System.Object.h>

#include <cstdio>
#include <cstdlib>
#include <string>
#include <iostream>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#endif

namespace cil2cpp {
namespace System {

// Initialize console for UTF-8 output on Windows
static bool g_console_initialized = false;

static void init_console() {
    if (g_console_initialized) return;

#ifdef _WIN32
    // Set console code page to UTF-8
    SetConsoleOutputCP(65001);
#endif

    g_console_initialized = true;
}

static void print_string(String* str, FILE* stream = stdout) {
    if (!str) {
        return;
    }

    char* utf8 = string_to_utf8(str);
    if (utf8) {
        fputs(utf8, stream);
        std::free(utf8);
    }
}

void Console_WriteLine() {
    init_console();
    putchar('\n');
}

void Console_WriteLine(String* value) {
    init_console();
    print_string(value);
    putchar('\n');
}

void Console_WriteLine(Int32 value) {
    init_console();
    printf("%d\n", value);
}

void Console_WriteLine(Int64 value) {
    init_console();
    printf("%lld\n", static_cast<long long>(value));
}

void Console_WriteLine(Single value) {
    init_console();
    printf("%g\n", static_cast<double>(value));
}

void Console_WriteLine(Double value) {
    init_console();
    printf("%g\n", value);
}

void Console_WriteLine(Boolean value) {
    init_console();
    printf("%s\n", value ? "True" : "False");
}

void Console_WriteLine(Object* value) {
    init_console();
    if (!value) {
        putchar('\n');
        return;
    }
    String* str = object_to_string(value);
    print_string(str);
    putchar('\n');
}

void Console_Write(String* value) {
    init_console();
    print_string(value);
}

void Console_Write(Int32 value) {
    init_console();
    printf("%d", value);
}

void Console_Write(Int64 value) {
    init_console();
    printf("%lld", static_cast<long long>(value));
}

void Console_Write(Single value) {
    init_console();
    printf("%g", static_cast<double>(value));
}

void Console_Write(Double value) {
    init_console();
    printf("%g", value);
}

void Console_Write(Boolean value) {
    init_console();
    printf("%s", value ? "True" : "False");
}

void Console_Write(Object* value) {
    init_console();
    if (!value) {
        return;
    }
    String* str = object_to_string(value);
    print_string(str);
}

String* Console_ReadLine() {
    init_console();

    std::string line;
    if (std::getline(std::cin, line)) {
        return string_create_utf8(line.c_str());
    }
    return nullptr;
}

Int32 Console_Read() {
    init_console();
    return getchar();
}

} // namespace System
} // namespace cil2cpp
