/**
 * CIL2CPP Runtime - System.Console
 */

#pragma once

#include "../string.h"
#include "../type_info.h"

namespace cil2cpp {
namespace System {

// Console.WriteLine overloads
void Console_WriteLine();
void Console_WriteLine(String* value);
void Console_WriteLine(Int32 value);
void Console_WriteLine(Int64 value);
void Console_WriteLine(Single value);
void Console_WriteLine(Double value);
void Console_WriteLine(Boolean value);
void Console_WriteLine(Object* value);

// Console.Write overloads
void Console_Write(String* value);
void Console_Write(Int32 value);
void Console_Write(Int64 value);
void Console_Write(Single value);
void Console_Write(Double value);
void Console_Write(Boolean value);
void Console_Write(Object* value);

// Console.ReadLine
String* Console_ReadLine();

// Console.Read
Int32 Console_Read();

} // namespace System
} // namespace cil2cpp
