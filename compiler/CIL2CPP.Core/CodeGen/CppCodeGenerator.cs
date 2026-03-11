using System.Text;
using CIL2CPP.Core.IR;

namespace CIL2CPP.Core.CodeGen;

/// <summary>
/// Generates C++ source code from IR module.
/// </summary>
public partial class CppCodeGenerator
{
    /// <summary>
    /// BCL fallback vtable entries for System.Object virtual methods.
    /// Used when a VTable slot has no user override (Method == null).
    /// </summary>
    private static readonly Dictionary<string, string> ObjectMethodFallbacks = new()
    {
        ["ToString"] = "(void*)cil2cpp::object_to_string",
        ["Equals"] = "(void*)cil2cpp::object_equals",
        ["GetHashCode"] = "(void*)cil2cpp::object_get_hash_code",
    };

    /// <summary>
    /// Unresolved generic parameter names that indicate an open generic type
    /// leaked through reachability analysis. These types cannot be compiled to C++.
    /// </summary>
    private static readonly string[] OpenGenericParamNames =
    [
        "TResult", "TKey", "TValue", "TSource", "TElement", "TOutput",
        "TAntecedentResult", "TContinuationResult", "TNewResult",
    ];

    /// <summary>
    /// Check if a type has unresolved generic parameters in its C++ name.
    /// Such types are open generics that leaked through and should be skipped.
    /// </summary>
    private static bool HasUnresolvedGenericParams(IRType type)
    {
        var name = type.CppName;
        foreach (var param in OpenGenericParamNames)
        {
            // Check if the param name appears as a word boundary in the C++ name
            var idx = name.IndexOf(param, StringComparison.Ordinal);
            if (idx >= 0)
            {
                // Verify it's not a substring of a longer word (e.g., "ResultHandler")
                var afterIdx = idx + param.Length;
                if (afterIdx >= name.Length || !char.IsLetterOrDigit(name[afterIdx]))
                    return true;
            }
        }

        // Also check for [] in CppName (invalid C++ identifier)
        if (name.Contains("[]"))
            return true;

        return false;
    }

    /// <summary>
    /// Check if a type name looks like an unresolved generic parameter.
    /// Pattern: T + uppercase letter + optional alphanumeric/digits, NO underscores.
    /// Examples: TOther, TArg1, TKey, TNegator, TContinuationResult
    /// </summary>
    private static bool IsUnresolvedGenericParam(string typeName)
    {
        // Single-letter "T" is a valid generic parameter name
        if (typeName == "T") return true;
        if (typeName.Length < 2) return false;
        if (typeName[0] != 'T' || !char.IsUpper(typeName[1])) return false;
        if (typeName.Contains('_')) return false;
        // Generic params follow PascalCase: T + one uppercase + immediately lowercase.
        // E.g., TResult, TKey, TValue, TChar, TTo, TNegator.
        // Reject acronym-prefixed segments like TOKENRef (T + multiple uppercase = acronym).
        if (typeName.Length >= 3 && char.IsUpper(typeName[2]))
            return false;
        // All chars must be letters or digits, and must contain at least one lowercase letter.
        // Real generic params are PascalCase (TResult, TKey, TValue) — all-caps words like
        // TIME, ZONE, INFORMATION (from Interop struct names) are not generic params.
        bool hasLower = false;
        for (int i = 2; i < typeName.Length; i++)
        {
            if (!char.IsLetterOrDigit(typeName[i])) return false;
            if (char.IsLower(typeName[i])) hasLower = true;
        }
        return hasLower;
    }

    /// <summary>
    /// Check if a mangled type name contains an unresolved generic parameter segment.
    /// E.g., "AsyncStateMachineBox_1_System_IO_Stream_TStateMachine" contains "TStateMachine".
    /// These types are forward-declared but never get struct definitions, causing C2027.
    /// </summary>
    private static bool MangledNameContainsUnresolvedGenericParam(string mangledName)
    {
        // Split by underscore and check each segment
        int start = 0;
        for (int i = 0; i <= mangledName.Length; i++)
        {
            if (i == mangledName.Length || mangledName[i] == '_')
            {
                if (i > start)
                {
                    var segment = mangledName[start..i];
                    if (IsUnresolvedGenericParam(segment))
                        return true;
                }
                start = i + 1;
            }
        }
        return false;
    }

    /// <summary>
    /// Check if a name is a valid C++ identifier (no brackets, ampersands, parens, etc.).
    /// Used to filter out mangled names from IL types that contain array/ref/pointer syntax.
    /// </summary>
    private static bool IsValidCppIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        // C++ identifiers can only contain letters, digits, underscores
        // Also allow :: for namespace-qualified names
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == ':')
                continue;
            return false;
        }
        return true;
    }

    private readonly IRModule _module;
    private readonly BuildConfiguration _config;

    /// <summary>
    /// Set of all declared C++ function names (populated during header generation).
    /// Used by source generation to filter out methods that call undeclared functions.
    /// </summary>
    private HashSet<string> _declaredFunctionNames = new();

    /// <summary>
    /// Function names called from method bodies but not declared (NOT_IN_MODULE or INVALID_SIGNATURE).
    /// Populated during header generation by GenerateMissingMethodStubs.
    /// Used by source generation to skip methods that call undeclared functions.
    /// </summary>
    private HashSet<string> _undeclaredFunctionNames = new();

    /// <summary>
    /// Map of function name → set of declared parameter counts (populated during header generation).
    /// Used to detect overload mismatches (calling a function with wrong number of arguments).
    /// </summary>
    private Dictionary<string, HashSet<int>> _declaredFunctionParamCounts = new();

    /// <summary>
    /// Opaque Span/ReadOnlySpan stub type names that need trivial method implementations
    /// generated in the source file (get_Length, get_IsEmpty).
    /// </summary>
    private List<string> _opaqueSpanStubs = new();

    /// <summary>
    /// Stub names for unresolved interface method implementations (null MethodImpl entries).
    /// Key: "TypeCppName|InterfaceCppName|slotIndex", Value: generated stub function name.
    /// </summary>
    private Dictionary<string, string> _interfaceStubNames = new();

    /// <summary>
    /// SafeHandleMarshaller ManagedToUnmanagedIn opaque stubs that need method implementations
    /// generated in the source file (FromManaged, ToUnmanaged, Free).
    /// </summary>
    private List<string> _opaqueSafeHandleMarshallerStubs = new();

    /// <summary>
    /// Set of type names that have full struct definitions emitted in the header.
    /// Types only forward-declared (no body) are NOT in this set.
    /// Populated during header generation, used by source generation to skip stubs
    /// that use undefined types by value.
    /// </summary>
    private HashSet<string> _emittedStructDefs = new();

    public CppCodeGenerator(IRModule module, BuildConfiguration? config = null)
    {
        _module = module;
        _config = config ?? BuildConfiguration.Release;
    }

    /// <summary>
    /// Set of emitted method signatures across all source files.
    /// Populated by data file (P/Invoke) and method files, consumed by stub file.
    /// </summary>
    private readonly HashSet<string> _emittedMethodSignatures = new();

    /// <summary>
    /// Filtered user types (deduped, no open generics). Shared across all generation phases.
    /// </summary>
    private List<IRType> _userTypes = new();

    /// <summary>
    /// Known type names for stub/error checking. Populated during data file generation.
    /// </summary>
    private HashSet<string> _knownTypeNames = new();

    /// <summary>
    /// Types forward-declared in the header (from method signatures, fields, and locals).
    /// Populated during header generation, used to expand _knownTypeNames.
    /// </summary>
    private HashSet<string> _headerForwardDeclared = new();

    /// <summary>
    /// Auto-discovered TypeInfo declarations — types referenced via _TypeInfo in method bodies
    /// but not in userTypes or PrimitiveTypeInfos. Populated during header generation.
    /// </summary>
    private HashSet<string> _autoTypeInfoDecls = new();

    /// <summary>
    /// All TypeInfo CppNames referenced from method bodies (via _TypeInfo pattern scan).
    /// Used for TypeInfo tiering: types whose _TypeInfo is never referenced get minimal TypeInfo.
    /// Populated during header generation by CollectTypeInfoRefs().
    /// </summary>
    private HashSet<string> _referencedTypeInfoNames = new();

    /// <summary>
    /// All TypeInfo CppNames that have extern declarations in the header.
    /// Used by RenderedBodyError to detect references to undeclared TypeInfo globals.
    /// Populated during header generation.
    /// </summary>
    private HashSet<string> _allDeclaredTypeInfoNames = new();

    /// <summary>
    /// Minimum IR instructions per method partition. Each TU re-parses the full header,
    /// so partitions need enough method code to amortize that overhead.
    /// ~20000 instructions ≈ 13k-17k C++ lines per partition (ratio ~0.7 lines/instruction).
    /// </summary>
    private const int MinInstructionsPerPartition = 20000;

    /// <summary>
    /// Generate all C++ files for the module.
    /// </summary>
    public GeneratedOutput Generate()
    {
        var output = new GeneratedOutput();

        // Generate header file with all type declarations
        output.HeaderFile = GenerateHeader();

        // Build shared userTypes list (used by all source generators)
        var seenTypeNames = new HashSet<string>();
        _userTypes = _module.Types
            .Where(t => !CppNameMapper.IsCompilerGeneratedType(t.ILFullName))
            .Where(t => !HasUnresolvedGenericParams(t))
            .Where(t => seenTypeNames.Add(t.CppName))
            .ToList();

        // Build known type set — union of all types that have definitions in the header.
        // Start with _emittedStructDefs (set by GenerateHeader): includes all struct definitions,
        // enum definitions, delegate aliases, opaque stubs, and runtime-provided type aliases.
        _knownTypeNames = new HashSet<string>(_emittedStructDefs);
        foreach (var t in _userTypes)
            _knownTypeNames.Add(t.CppName);
        foreach (var ilName in RuntimeTypeRegistry.GetILNames(RuntimeTypeFlags.RuntimeProvided))
            _knownTypeNames.Add(CppNameMapper.MangleTypeName(ilName));
        foreach (var (mangled, _) in RuntimeTypeRegistry.GetTypeAliases())
            _knownTypeNames.Add(mangled);
        // External BCL enum types (emitted as "using X = int32_t" aliases in header)
        foreach (var (mangled, _) in _module.ExternalEnumTypes)
            _knownTypeNames.Add(mangled);
        // Primitive types with TypeInfo definitions (emitted in data file)
        foreach (var entry in _module.PrimitiveTypeInfos.Values)
            _knownTypeNames.Add(entry.CppMangledName);
        // Array initializer data blobs (declared as extern const unsigned char[] in header)
        foreach (var blob in _module.ArrayInitDataBlobs)
            _knownTypeNames.Add(blob.Id);
        // Auto-discovered TypeInfo types (from method body _TypeInfo references)
        foreach (var name in _autoTypeInfoDecls)
            _knownTypeNames.Add(name);
        // NOTE: _headerForwardDeclared types are NOT added to _knownTypeNames here.
        // Forward-declared types only support pointer usage (Type*), not value usage (sizeof/locals).

        // Generate split source files
        output.DataFile = GenerateDataFile();
        output.MethodFiles = GenerateMethodFiles();
        // CoreRuntimeTypes method bodies are provided by the runtime library (core_methods.cpp).
        // No runtime_glue.cpp is generated — the compiler only generates stubs for
        // non-CoreRuntimeTypes methods that couldn't be compiled from IL.
        output.StubFile = GenerateStubFile();

        // Generate main entry point only for executable projects (with entry point)
        if (_module.EntryPoint != null)
        {
            output.MainFile = GenerateMain();
        }

        // Generate CMakeLists.txt
        output.CMakeFile = GenerateCMakeLists(output);

        return output;
    }

    private GeneratedFile GenerateMain()
    {
        var sb = new StringBuilder();

        sb.AppendLine("// Generated by CIL2CPP - DO NOT EDIT");
        sb.AppendLine($"// Entry point for {_module.Name}");
        sb.AppendLine();
        sb.AppendLine($"#include \"{_module.Name}.h\"");
        sb.AppendLine();
        sb.AppendLine("int main(int argc, char* argv[]) {");
        sb.AppendLine("    cil2cpp::runtime_init();");
        sb.AppendLine("    cil2cpp::runtime_set_args(argc, argv);");
        sb.AppendLine();

        // Initialize string literals
        if (_module.StringLiterals.Count > 0)
        {
            sb.AppendLine("    __init_string_literals();");
        }
        // Patch runtime TypeInfos with codegen VTable/interface data
        sb.AppendLine("    __init_runtime_vtables();");
        sb.AppendLine();

        // Call entry point
        if (_module.EntryPoint != null)
        {
            sb.AppendLine($"    // Call {_module.EntryPoint.DeclaringType?.ILFullName}::{_module.EntryPoint.Name}");
            sb.AppendLine($"    {_module.EntryPoint.CppName}();");
        }
        else
        {
            sb.AppendLine("    // WARNING: No entry point found");
        }

        sb.AppendLine();
        sb.AppendLine("    cil2cpp::runtime_shutdown();");
        sb.AppendLine("    return 0;");
        sb.AppendLine("}");

        return new GeneratedFile
        {
            FileName = "main.cpp",
            Content = sb.ToString()
        };
    }

    private GeneratedFile GenerateCMakeLists(GeneratedOutput output)
    {
        var sb = new StringBuilder();
        var projectName = _module.Name;
        bool isExe = _module.EntryPoint != null;
        var linkVisibility = isExe ? "PRIVATE" : "PUBLIC";

        sb.AppendLine("# Generated by CIL2CPP - DO NOT EDIT");
        sb.AppendLine("cmake_minimum_required(VERSION 3.20)");
        sb.AppendLine($"project({projectName} CXX)");
        sb.AppendLine();
        sb.AppendLine("set(CMAKE_CXX_STANDARD 20)");
        sb.AppendLine("set(CMAKE_CXX_STANDARD_REQUIRED ON)");
        sb.AppendLine();

        // Default build type
        sb.AppendLine("if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)");
        sb.AppendLine($"    set(CMAKE_BUILD_TYPE \"{_config.ConfigurationName}\" CACHE STRING \"Build type\" FORCE)");
        sb.AppendLine("    set_property(CACHE CMAKE_BUILD_TYPE PROPERTY STRINGS \"Debug\" \"Release\")");
        sb.AppendLine("endif()");
        sb.AppendLine();

        // Target: executable or static library
        if (isExe)
        {
            sb.AppendLine($"add_executable({projectName}");
            sb.AppendLine("    main.cpp");
            foreach (var sf in output.AllSourceFiles)
                sb.AppendLine($"    {sf.FileName}");
            sb.AppendLine(")");
        }
        else
        {
            sb.AppendLine($"add_library({projectName} STATIC");
            foreach (var sf in output.AllSourceFiles)
                sb.AppendLine($"    {sf.FileName}");
            sb.AppendLine(")");
            sb.AppendLine();
            sb.AppendLine($"target_include_directories({projectName} PUBLIC");
            sb.AppendLine("    $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}>");
            sb.AppendLine(")");
        }
        sb.AppendLine();

        // CIL2CPP Runtime via find_package
        sb.AppendLine("find_package(cil2cpp REQUIRED)");
        sb.AppendLine($"target_link_libraries({projectName} {linkVisibility} cil2cpp::runtime)");
        sb.AppendLine();

        // P/Invoke native library linking (filter out .NET internal modules)
        var pinvokeModules = _module.Types
            .SelectMany(t => t.Methods)
            .Where(m => m.IsPInvoke && !string.IsNullOrEmpty(m.PInvokeModule))
            .Select(m => m.PInvokeModule!)
            .Where(m => !InternalPInvokeModules.Contains(m))
            .Distinct()
            .ToList();
        if (pinvokeModules.Count > 0)
        {
            sb.AppendLine("# P/Invoke native libraries");
            foreach (var mod in pinvokeModules)
            {
                // Strip .dll/.so extension for CMake library name
                var libName = mod;
                if (libName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    libName = libName[..^4];
                else if (libName.EndsWith(".so", StringComparison.OrdinalIgnoreCase))
                    libName = libName[..^3];
                // Map DLL names to their SDK import library equivalents
                // sspicli.dll exports SSPI functions but has no .lib; use Secur32.lib instead
                libName = libName switch
                {
                    "sspicli" => "Secur32",
                    _ => libName
                };
                sb.AppendLine($"target_link_libraries({projectName} PRIVATE {libName})");
            }
            sb.AppendLine();
        }

        // Debug/Release settings for generated code
        sb.AppendLine($"target_compile_definitions({projectName} {linkVisibility}");
        sb.AppendLine("    $<$<CONFIG:Debug>:CIL2CPP_DEBUG>)");
        sb.AppendLine();

        sb.AppendLine("if(MSVC)");
        sb.AppendLine($"    target_compile_options({projectName} PRIVATE /utf-8 /MP /bigobj");
        sb.AppendLine("        $<$<CONFIG:Debug>:/Zi /Od /RTC1>");
        sb.AppendLine("        $<$<CONFIG:Release>:/O2 /DNDEBUG>");
        sb.AppendLine("    )");
        if (isExe)
        {
            sb.AppendLine($"    target_link_options({projectName} PRIVATE");
            sb.AppendLine("        $<$<CONFIG:Debug>:/DEBUG>");
            // BCL cctor chains can be deep; use 8MB stack instead of default 1MB
            sb.AppendLine("        /STACK:8388608)");
        }
        sb.AppendLine("else()");
        sb.AppendLine($"    target_compile_options({projectName} PRIVATE");
        sb.AppendLine("        $<$<CONFIG:Debug>:-g -O0>");
        sb.AppendLine("        $<$<CONFIG:Release>:-O2 -DNDEBUG>");
        sb.AppendLine("    )");
        if (isExe)
        {
            // HACK: BCL cctor chains (e.g. HttpClient) can be very deep; default stack may overflow
            sb.AppendLine($"    target_link_options({projectName} PRIVATE -Wl,-z,stacksize=8388608)");
        }
        sb.AppendLine("endif()");

        // Copy ICU DLLs to output directory (Windows only)
        // ICU::uc and ICU::dt SHARED IMPORTED targets are created by cil2cppConfig.cmake
        if (isExe)
        {
            sb.AppendLine();
            sb.AppendLine("# Copy ICU DLLs to output directory (Windows only)");
            sb.AppendLine("if(WIN32 AND TARGET ICU::uc)");
            sb.AppendLine($"    add_custom_command(TARGET {projectName} POST_BUILD");
            sb.AppendLine("        COMMAND ${CMAKE_COMMAND} -E copy_if_different");
            sb.AppendLine("            $<TARGET_PROPERTY:ICU::uc,IMPORTED_LOCATION>");
            sb.AppendLine($"            $<TARGET_FILE_DIR:{projectName}>");
            sb.AppendLine("        COMMAND ${CMAKE_COMMAND} -E copy_if_different");
            sb.AppendLine("            $<TARGET_PROPERTY:ICU::dt,IMPORTED_LOCATION>");
            sb.AppendLine($"            $<TARGET_FILE_DIR:{projectName}>");
            sb.AppendLine("        COMMAND ${CMAKE_COMMAND} -E copy_if_different");
            sb.AppendLine("            $<TARGET_PROPERTY:ICU::in,IMPORTED_LOCATION>");
            sb.AppendLine($"            $<TARGET_FILE_DIR:{projectName}>");
            sb.AppendLine("    )");
            sb.AppendLine("endif()");
        }

        return new GeneratedFile
        {
            FileName = "CMakeLists.txt",
            Content = sb.ToString()
        };
    }

    private static string EscapeString(string s)
    {
        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default:
                    if (char.IsHighSurrogate(ch) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        // Surrogate pair → combine into a single 32-bit UCN (\UXXXXXXXX).
                        // MSVC C3850: \uD800-\uDFFF are invalid as 16-bit universal character names.
                        int codePoint = char.ConvertToUtf32(ch, s[i + 1]);
                        sb.Append($"\\U{codePoint:X8}");
                        i++; // skip low surrogate
                    }
                    else if (ch >= 0xD800 && ch <= 0xDFFF)
                    {
                        // Lone surrogate (no valid pair) — replace with U+FFFD.
                        // Surrogates are invalid as \uXXXX (C3850) and too large for \xXX (C7744).
                        sb.Append("\\uFFFD");
                    }
                    else if (ch > 127)
                    {
                        // Escape non-ASCII as universal character names for MSVC compatibility
                        sb.Append($"\\u{(int)ch:X4}");
                    }
                    else if (ch < 32)
                    {
                        // Escape all control characters (0x01-0x1F) as hex.
                        // Raw 0x1A (Ctrl+Z) is Windows EOF marker — causes C1004.
                        sb.Append($"\\x{(int)ch:x2}");
                    }
                    else
                    {
                        sb.Append(ch);
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Check if a method's C++ signature contains types that are not valid C++ identifiers.
    /// This catches unresolved generic params (TOther, TKey) and IL function pointer types (method...ptr).
    /// Methods with invalid signatures should not be declared or compiled — the root cause
    /// is either a tree-shaking issue (shouldn't be reachable) or a missing compiler feature.
    /// </summary>
    private static bool HasInvalidCppSignature(IR.IRMethod method)
    {
        // Check method name for unresolved generic params (e.g., MemoryMarshal_Cast_Byte_TResult)
        if (MangledNameContainsUnresolvedGenericParam(method.CppName))
            return true;

        // Check all parameter types and return type
        foreach (var param in method.Parameters)
        {
            if (IsInvalidCppType(param.CppTypeName))
                return true;
        }
        if (!string.IsNullOrEmpty(method.ReturnTypeCpp) && IsInvalidCppType(method.ReturnTypeCpp))
            return true;
        // Check return type for unresolved generic params in compound names
        if (!string.IsNullOrEmpty(method.ReturnTypeCpp))
        {
            var retBase = method.ReturnTypeCpp.TrimEnd('*').Trim();
            if (MangledNameContainsUnresolvedGenericParam(retBase))
                return true;
        }
        // Check parameter types for unresolved generic params in compound names
        foreach (var param in method.Parameters)
        {
            if (!string.IsNullOrEmpty(param.CppTypeName))
            {
                var paramBase = param.CppTypeName.TrimEnd('*').Trim();
                if (MangledNameContainsUnresolvedGenericParam(paramBase))
                    return true;
            }
        }
        // Check local variable types for unresolved generic params
        // (e.g., DefaultBinder.BindToMethod has locals of type T)
        foreach (var local in method.Locals)
        {
            if (!string.IsNullOrEmpty(local.CppTypeName))
            {
                if (IsInvalidCppType(local.CppTypeName))
                    return true;
                var localBase = local.CppTypeName.TrimEnd('*').Trim();
                if (MangledNameContainsUnresolvedGenericParam(localBase))
                    return true;
                // void as a local type causes sizeof(void) — illegal in C++
                // (void* is fine, only bare void is problematic)
                if (local.CppTypeName.Trim() == "void")
                    return true;
            }
        }
        return false;
    }


    /// <summary>
    /// Detect methods on generic type specializations where the method body uses
    /// a different specialization's functions/types than what the parent type's
    /// generic parameter would suggest.
    /// E.g., MemberInfoCache&lt;RuntimePropertyInfo&gt;.PopulateMethods() creates
    /// RuntimeMethodInfo objects and calls ListBuilder&lt;RuntimeMethodInfo&gt;.Add(),
    /// but the casts use RuntimePropertyInfo (from generic substitution) — causing C2664.
    /// </summary>
    private static bool HasGenericBodyTypeConflict(IR.IRType type, IR.IRMethod method)
    {
        // Only check generic types (CppName contains arity marker like _1_)
        if (!type.IsGenericInstance) return false;
        if (method.BasicBlocks.Count == 0) return false;

        // Extract the generic arg(s) from the type's CppName
        // E.g., "MemberInfoCache_1_System_Reflection_RuntimePropertyInfo" → "RuntimePropertyInfo"
        var typeCppName = type.CppName;
        var typeIlName = type.ILFullName;

        // Get the generic args from ILFullName (e.g., from angle brackets)
        var typeArgs = new List<string>();
        var angleBracket = typeIlName.IndexOf('<');
        if (angleBracket > 0 && typeIlName.EndsWith(">"))
        {
            var argsStr = typeIlName[(angleBracket + 1)..^1];
            typeArgs = CppNameMapper.ParseGenericArgs(argsStr);
        }
        if (typeArgs.Count == 0) return false;

        // For each IRCall/IRNewObj in the body, check if a different specialization
        // of the same generic base type is used, creating type conflicts
        var typeArgCppNames = typeArgs.Select(CppNameMapper.MangleTypeName).ToHashSet();

        // Collect all MemberInfo subtypes referenced in method body (newobj, call targets)
        var bodyMemberInfoTypes = new HashSet<string>();
        foreach (var block in method.BasicBlocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr is IR.IRNewObj newObj && IsMemberInfoSubtype(newObj.TypeCppName))
                {
                    bodyMemberInfoTypes.Add(newObj.TypeCppName);
                }
                else if (instr is IR.IRCall call && !string.IsNullOrEmpty(call.FunctionName))
                {
                    // Check if the call target uses a specific MemberInfo specialization
                    // E.g., "ListBuilder_1_RuntimeMethodInfo_Add" → uses RuntimeMethodInfo
                    foreach (var memberType in MemberInfoSubtypeNames)
                    {
                        if (call.FunctionName.Contains(memberType))
                        {
                            bodyMemberInfoTypes.Add(memberType);
                        }
                    }
                }
            }
        }

        // If the body references MemberInfo subtypes that differ from the generic arg,
        // this method has type conflicts from generic substitution
        foreach (var bodyType in bodyMemberInfoTypes)
        {
            foreach (var argCpp in typeArgCppNames)
            {
                if (IsMemberInfoSubtype(argCpp) && argCpp != bodyType)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Known MemberInfo subtype C++ names — used for generic body conflict detection.
    /// </summary>
    private static readonly string[] MemberInfoSubtypeNames =
    {
        "System_Reflection_RuntimeMethodInfo",
        "System_Reflection_RuntimeConstructorInfo",
        "System_Reflection_RuntimeFieldInfo",
        "System_Reflection_RuntimePropertyInfo",
        "System_Reflection_RuntimeEventInfo",
        "System_RuntimeType",
    };

    /// <summary>
    /// Check if a C++ type name is a System.Reflection MemberInfo subtype.
    /// Used for generic body conflict detection.
    /// </summary>
    private static bool IsMemberInfoSubtype(string cppTypeName)
    {
        return cppTypeName.StartsWith("System_Reflection_Runtime") ||
               cppTypeName == "System_RuntimeType";
    }

    /// <summary>
    /// Check if a method body calls any function that is NOT_IN_MODULE or INVALID_SIGNATURE.
    /// These methods can't compile because the called functions don't have C++ declarations.
    /// Populated during header generation by GenerateMissingMethodStubs.
    /// </summary>
    private bool CallsUndeclaredFunction(IR.IRMethod method)
    {
        if (_undeclaredFunctionNames.Count == 0) return false;
        if (method.BasicBlocks.Count == 0) return false;

        foreach (var block in method.BasicBlocks)
        {
            foreach (var instr in block.Instructions)
            {
                if (instr is IR.IRCall call && !string.IsNullOrEmpty(call.FunctionName)
                    && !call.IsVirtual // Virtual calls use VTable dispatch; FunctionName not in rendered C++
                    && _undeclaredFunctionNames.Contains(call.FunctionName)
                    && !IsSimdDeadCodeFunction(call.FunctionName)
                    && !IsEventSourceDiagnosticFunction(call.FunctionName))
                {
                    return true;
                }
                // Note: All SIMD-related calls (intrinsics, static helpers, AND generic type
                // instance methods like Vector128<T>.op_*, get_Zero) are exempted because they
                // only appear in feature-switch-guarded dead branches (IsSupported=false on AOT).
                // They're replaced with default values at render time in GenerateMethodImpl.
                if (instr is IR.IRNewObj newObj && !string.IsNullOrEmpty(newObj.CtorName)
                    && _undeclaredFunctionNames.Contains(newObj.CtorName))
                    return true;
                if (instr is IR.IRLoadFunctionPointer ldftn && !string.IsNullOrEmpty(ldftn.MethodCppName)
                    && !ldftn.IsVirtual // Virtual ldftn uses VTable lookup
                    && _undeclaredFunctionNames.Contains(ldftn.MethodCppName))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// SIMD intrinsic functions (X86/Arm/Wasm) are always in feature-switch-guarded dead-code
    /// branches. Their IRCall instructions exist in the IR but are unreachable at runtime
    /// (IsSupported always returns false on AOT). Don't block method emission for these.
    /// </summary>
    private static bool IsSimdIntrinsicFunction(string functionName)
    {
        return functionName.StartsWith("System_Runtime_Intrinsics_X86_")
            || functionName.StartsWith("System_Runtime_Intrinsics_Arm_")
            || functionName.StartsWith("System_Runtime_Intrinsics_Wasm_");
    }

    /// <summary>
    /// EventSource diagnostic methods (NativeRuntimeEventSource, NetEventSource, etc.) are always
    /// behind EventSource.IsEnabled() guards which always return false in AOT. These are dead code
    /// like SIMD intrinsics — don't block method emission for referencing undeclared EventSource methods.
    /// </summary>
    private static bool IsEventSourceDiagnosticFunction(string functionName)
    {
        return functionName.StartsWith("System_Diagnostics_Tracing_NativeRuntimeEventSource_")
            || functionName.StartsWith("System_Net_NetEventSource_")
            || functionName.StartsWith("System_Net_Sockets_NetEventSource_");
    }

    /// <summary>
    /// All SIMD-related functions: hardware intrinsics (X86/Arm/Wasm), generic container methods
    /// (Vector128&lt;T&gt;, Vector256&lt;T&gt;, etc.), non-generic helper methods (Vector128.Create, etc.),
    /// and BCL helper classes that only serve SIMD code paths (PackedSpanHelpers, IndexOfAnyAsciiSearcher).
    /// These are always in dead-code branches (IsSupported=false on AOT). Undeclared calls to
    /// these functions are replaced with default values at render time.
    /// </summary>
    private static bool IsSimdDeadCodeFunction(string functionName)
    {
        return IsSimdIntrinsicFunction(functionName)
            || functionName.StartsWith("System_Runtime_Intrinsics_Vector64_1_")
            || functionName.StartsWith("System_Runtime_Intrinsics_Vector128_1_")
            || functionName.StartsWith("System_Runtime_Intrinsics_Vector256_1_")
            || functionName.StartsWith("System_Runtime_Intrinsics_Vector512_1_")
            || functionName.StartsWith("System_Numerics_Vector_1_")
            || functionName.StartsWith("System_Runtime_Intrinsics_Vector64_")
            || functionName.StartsWith("System_Runtime_Intrinsics_Vector128_")
            || functionName.StartsWith("System_Runtime_Intrinsics_Vector256_")
            || functionName.StartsWith("System_Runtime_Intrinsics_Vector512_")
            || functionName.StartsWith("System_Runtime_Intrinsics_Scalar_1_")
            // BCL helper classes that only serve SIMD-accelerated code paths.
            // Called behind Sse2.IsSupported / AdvSimd.IsSupported / Vector128.IsHardwareAccelerated
            // guards — always dead in AOT without SIMD support.
            || functionName.StartsWith("System_PackedSpanHelpers_")
            || functionName.StartsWith("System_SpanHelpers_ComputeFirstIndex_")
            || functionName.StartsWith("System_SpanHelpers_ComputeLastIndex_")
            || functionName.StartsWith("System_Buffers_IndexOfAnyAsciiSearcher_")
            || functionName.StartsWith("System_Text_Ascii_AllCharsInVectorAreAscii_")
            || functionName.StartsWith("System_Text_Ascii_ChangeWidthAndWriteTo_")
            || functionName.StartsWith("System_Text_Ascii_SignedLessThan_")
            || functionName.StartsWith("System_Text_Ascii_VectorContainsNonAsciiChar_");
    }

    /// <summary>
    /// Check if a C++ type name is invalid — either an unresolved generic param or a function pointer type.
    /// </summary>
    private static bool IsInvalidCppType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return false;
        var baseType = typeName.TrimEnd('*').Trim();
        if (string.IsNullOrEmpty(baseType)) return false;

        // Function pointer types: IL function pointers produce "method..." names
        if (baseType.StartsWith("method") && !baseType.Contains('_'))
            return true;
        if (typeName.Contains("method") && typeName.Contains("ptr"))
            return true;

        // Unresolved generic params: bare PascalCase identifiers without underscores
        // (e.g., TOther, TKey, TValue, TResult, TFrom, TTo)
        // Valid mangled .NET names always contain underscores (System_Int32, etc.)
        if (baseType.Length >= 2 && baseType[0] == 'T' && char.IsUpper(baseType[1])
            && !baseType.Contains('_') && !baseType.StartsWith("cil2cpp::"))
            return true;

        // Unresolved method-level generic params: !!0, !!1, etc.
        if (baseType.StartsWith("!!"))
            return true;

        return false;
    }

}

public class GeneratedOutput
{
    public GeneratedFile HeaderFile { get; set; } = new();

    /// <summary>
    /// Data file: string literals, array init data, static fields, TypeInfo, VTable,
    /// interface data, reflection metadata, ensure_cctor, P/Invoke wrappers.
    /// </summary>
    public GeneratedFile DataFile { get; set; } = new();

    /// <summary>
    /// Method implementation files (partitioned for parallel compilation).
    /// </summary>
    public List<GeneratedFile> MethodFiles { get; set; } = new();

    /// <summary>
    /// Stub file: placeholders for methods that should be eliminated by tree-shaking.
    /// CoreRuntimeTypes methods are NOT included — the runtime library provides them.
    /// </summary>
    public GeneratedFile StubFile { get; set; } = new();

    /// <summary>
    /// Backward-compatible alias — returns DataFile for tests checking data content.
    /// </summary>
    public GeneratedFile SourceFile => DataFile;

    /// <summary>
    /// Concatenation of all source file contents (data + methods + stubs).
    /// Useful for tests that search across all generated C++ code.
    /// </summary>
    public string AllSourceContent
    {
        get
        {
            var sb = new StringBuilder();
            sb.Append(DataFile.Content);
            foreach (var mf in MethodFiles)
                sb.Append(mf.Content);
            sb.Append(StubFile.Content);
            return sb.ToString();
        }
    }

    /// <summary>
    /// All source files (data + methods + stubs) for CMake and WriteToDirectory.
    /// </summary>
    public IEnumerable<GeneratedFile> AllSourceFiles
    {
        get
        {
            yield return DataFile;
            foreach (var mf in MethodFiles)
                yield return mf;
            yield return StubFile;
        }
    }

    public GeneratedFile? MainFile { get; set; }
    public GeneratedFile? CMakeFile { get; set; }

    /// <summary>
    /// Write all generated files to a directory.
    /// </summary>
    public void WriteToDirectory(string outputDir)
    {
        Directory.CreateDirectory(outputDir);

        // Clean stale generated files from previous runs.
        // When dedup reduces method count, the partition count shrinks, leaving
        // orphan methods_N.cpp files that cause duplicate symbol linker errors.
        var generatedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { HeaderFile.FileName };
        foreach (var sf in AllSourceFiles) generatedFileNames.Add(sf.FileName);
        if (MainFile != null) generatedFileNames.Add(MainFile.FileName);
        if (CMakeFile != null) generatedFileNames.Add(CMakeFile.FileName);
        foreach (var existing in Directory.GetFiles(outputDir, "*.cpp")
            .Concat(Directory.GetFiles(outputDir, "*.h")))
        {
            var name = Path.GetFileName(existing);
            if (!generatedFileNames.Contains(name))
                File.Delete(existing);
        }

        File.WriteAllText(Path.Combine(outputDir, HeaderFile.FileName), HeaderFile.Content);
        foreach (var sourceFile in AllSourceFiles)
        {
            File.WriteAllText(Path.Combine(outputDir, sourceFile.FileName), sourceFile.Content);
        }
        if (MainFile != null)
        {
            File.WriteAllText(Path.Combine(outputDir, MainFile.FileName), MainFile.Content);
        }
        if (CMakeFile != null)
        {
            File.WriteAllText(Path.Combine(outputDir, CMakeFile.FileName), CMakeFile.Content);
        }
    }
}

public class GeneratedFile
{
    public string FileName { get; set; } = "";
    public string Content { get; set; } = "";
}
