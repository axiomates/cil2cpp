/**
 * CIL2CPP Runtime - System.IO Support
 *
 * ICalls for File, Path, and Directory operations.
 * Bypasses the BCL FileStream/SafeFileHandle chain by implementing
 * public API methods directly using platform APIs.
 */

#pragma once

#include "types.h"
#include "string.h"
#include "array.h"

namespace cil2cpp {
namespace icall {

// ===== System.IO.File =====
bool File_Exists(String* path);
String* File_ReadAllText(String* path);
String* File_ReadAllText2(String* path, void* encoding); // with Encoding parameter
void File_WriteAllText(String* path, String* contents);
void File_WriteAllText2(String* path, String* contents, void* encoding);
Object* File_ReadAllBytes(String* path);  // returns byte[]
void File_WriteAllBytes(String* path, Object* bytes);
void File_Delete(String* path);
void File_Copy(String* srcPath, String* destPath, bool overwrite);
void File_Move(String* srcPath, String* destPath, bool overwrite);
Object* File_ReadAllLines(String* path);  // returns string[]
void File_AppendAllText(String* path, String* contents);

// ===== System.IO.Directory =====
bool Directory_Exists(String* path);
Object* Directory_CreateDirectory(String* path);  // returns DirectoryInfo (FIXME: returns nullptr)

// ===== System.IO.Path =====
String* Path_GetFullPath(String* path);
String* Path_GetDirectoryName(String* path);
String* Path_GetFileName(String* path);
String* Path_GetFileNameWithoutExtension(String* path);
String* Path_GetExtension(String* path);
String* Path_GetTempPath();
String* Path_Combine2(String* path1, String* path2);
String* Path_Combine3(String* path1, String* path2, String* path3);

} // namespace icall
} // namespace cil2cpp
