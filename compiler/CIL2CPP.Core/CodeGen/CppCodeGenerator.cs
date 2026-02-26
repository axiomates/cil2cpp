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
        if (typeName.Length < 2) return false;
        if (typeName[0] != 'T' || !char.IsUpper(typeName[1])) return false;
        if (typeName.Contains('_')) return false;
        // All chars must be letters or digits
        for (int i = 2; i < typeName.Length; i++)
        {
            if (!char.IsLetterOrDigit(typeName[i])) return false;
        }
        return true;
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
    /// Set of type names that have full struct definitions emitted in the header.
    /// Types only forward-declared (no body) are NOT in this set.
    /// Populated during header generation, used by source generation to skip stubs
    /// that use undefined types by value.
    /// </summary>
    private HashSet<string> _emittedStructDefs = new();

    /// <summary>
    /// Tracks all methods that were stubbed during code generation, with reasons.
    /// Used to generate a diagnostic report (stubbed_methods.txt).
    /// </summary>
    private readonly List<StubbedMethodInfo> _stubbedMethods = new();

    /// <summary>
    /// Optional stub analyzer for detailed root-cause analysis (--analyze-stubs).
    /// When non-null, stubs are tracked with detailed causes and call graph edges.
    /// </summary>
    private StubAnalyzer? _stubAnalyzer;

    /// <summary>
    /// The analysis result after Generate() completes. Non-null only if analysis was enabled.
    /// </summary>
    public StubAnalysisResult? AnalysisResult { get; private set; }

    public CppCodeGenerator(IRModule module, BuildConfiguration? config = null, bool analyzeStubs = false)
    {
        _module = module;
        _config = config ?? BuildConfiguration.Release;
        if (analyzeStubs)
            _stubAnalyzer = new StubAnalyzer();
    }

    /// <summary>
    /// Generate analysis report text. Only valid after Generate() with analyzeStubs=true.
    /// </summary>
    public string? GetAnalysisReport()
    {
        if (_stubAnalyzer == null || AnalysisResult == null) return null;
        return _stubAnalyzer.GenerateReport(AnalysisResult, _module.Name);
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
        foreach (var ilName in IRBuilder.RuntimeProvidedTypes)
            _knownTypeNames.Add(CppNameMapper.MangleTypeName(ilName));
        foreach (var (mangled, _) in GetRuntimeProvidedTypeAliases())
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
        // NOTE: _headerForwardDeclared types are NOT added to _knownTypeNames here.
        // Forward-declared types only support pointer usage (Type*), not value usage (sizeof/locals).
        // The HasUnknownBodyReferences gate checks _headerForwardDeclared separately for pointer locals.

        // Generate split source files
        output.DataFile = GenerateDataFile();
        output.MethodFiles = GenerateMethodFiles();
        output.StubFile = GenerateStubFile();

        // Generate main entry point only for executable projects (with entry point)
        if (_module.EntryPoint != null)
        {
            output.MainFile = GenerateMain();
        }

        // Generate CMakeLists.txt
        output.CMakeFile = GenerateCMakeLists(output);

        // Feed IR-level stubs (CLR-internal dependencies) into the analyzer
        if (_stubAnalyzer != null)
        {
            FeedIrStubsToAnalyzer();
            CollectCallGraphEdges();
            AnalysisResult = _stubAnalyzer.Analyze();
        }

        // Generate stub diagnostics report
        if (_stubbedMethods.Count > 0)
        {
            output.StubReportFile = GenerateStubReport();
            PrintStubSummary();
        }

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
            sb.AppendLine();
        }

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
                sb.AppendLine($"target_link_libraries({projectName} PRIVATE {libName})");
            }
            sb.AppendLine();
        }

        // Debug/Release settings for generated code
        sb.AppendLine($"target_compile_definitions({projectName} {linkVisibility}");
        sb.AppendLine("    $<$<CONFIG:Debug>:CIL2CPP_DEBUG>)");
        sb.AppendLine();

        sb.AppendLine("if(MSVC)");
        sb.AppendLine($"    target_compile_options({projectName} PRIVATE /utf-8 /MP");
        sb.AppendLine("        $<$<CONFIG:Debug>:/Zi /Od /RTC1>");
        sb.AppendLine("        $<$<CONFIG:Release>:/O2 /DNDEBUG>");
        sb.AppendLine("    )");
        if (isExe)
        {
            sb.AppendLine($"    target_link_options({projectName} PRIVATE");
            sb.AppendLine("        $<$<CONFIG:Debug>:/DEBUG>)");
        }
        sb.AppendLine("else()");
        sb.AppendLine($"    target_compile_options({projectName} PRIVATE");
        sb.AppendLine("        $<$<CONFIG:Debug>:-g -O0>");
        sb.AppendLine("        $<$<CONFIG:Release>:-O2 -DNDEBUG>");
        sb.AppendLine("    )");
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
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"':  sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default:
                    if (ch > 127)
                    {
                        // Escape non-ASCII as universal character names for MSVC compatibility
                        sb.Append($"\\u{(int)ch:X4}");
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

    private GeneratedFile GenerateStubReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"// CIL2CPP Stub Report — {_stubbedMethods.Count} methods stubbed");
        sb.AppendLine($"// Assembly: {_module.Name}");
        sb.AppendLine();

        // Group by reason
        var groups = _stubbedMethods
            .GroupBy(m => m.Reason)
            .OrderByDescending(g => g.Count());

        foreach (var group in groups)
        {
            sb.AppendLine($"// === {group.Key} ({group.Count()}) ===");
            foreach (var m in group.OrderBy(m => m.TypeName).ThenBy(m => m.MethodName))
            {
                sb.AppendLine($"  {m.TypeName}::{m.MethodName}");
            }
            sb.AppendLine();
        }

        return new GeneratedFile
        {
            FileName = "stubbed_methods.txt",
            Content = sb.ToString()
        };
    }

    private void PrintStubSummary()
    {
        var groups = _stubbedMethods
            .GroupBy(m => m.Reason)
            .OrderByDescending(g => g.Count());

        Console.Error.WriteLine($"[CIL2CPP] {_stubbedMethods.Count} methods stubbed:");
        foreach (var group in groups)
        {
            Console.Error.WriteLine($"  {group.Key}: {group.Count()}");
        }
        Console.Error.WriteLine("  Details: stubbed_methods.txt");
    }

    private void TrackStub(IRMethod method, string reason)
    {
        _stubbedMethods.Add(new StubbedMethodInfo(
            method.DeclaringType?.ILFullName ?? "?",
            method.Name,
            method.CppName,
            reason
        ));
    }

    /// <summary>
    /// Track a stub with detailed root-cause information for the analyzer.
    /// Falls back to simple TrackStub when analyzer is not active.
    /// </summary>
    private void TrackStubDetailed(IRMethod method, string reason,
        StubRootCause rootCause, string detail)
    {
        TrackStub(method, reason);
        _stubAnalyzer?.AddStub(
            method.DeclaringType?.ILFullName ?? "?",
            method.Name,
            method.CppName,
            rootCause,
            detail
        );
    }

    /// <summary>
    /// Feed IR-level stubs (CLR-internal dependencies detected at IRBuilder Pass 6)
    /// into the StubAnalyzer. These stubs have IrStubReason set on the IRMethod.
    /// </summary>
    private void FeedIrStubsToAnalyzer()
    {
        if (_stubAnalyzer == null) return;

        foreach (var type in _module.Types)
        {
            foreach (var method in type.Methods)
            {
                if (method.IrStubReason != null)
                {
                    _stubAnalyzer.AddStub(
                        type.ILFullName,
                        method.Name,
                        method.CppName,
                        StubRootCause.ClrInternalType,
                        method.IrStubReason
                    );
                }
            }
        }
    }

    /// <summary>
    /// Collect call graph edges from all method bodies for cascade analysis.
    /// Each IRCall instruction creates an edge: caller → callee.
    /// </summary>
    private void CollectCallGraphEdges()
    {
        if (_stubAnalyzer == null) return;

        foreach (var type in _module.Types)
        {
            foreach (var method in type.Methods)
            {
                foreach (var block in method.BasicBlocks)
                {
                    foreach (var instr in block.Instructions)
                    {
                        if (instr is IR.IRCall call && !string.IsNullOrEmpty(call.FunctionName))
                        {
                            _stubAnalyzer.AddCallEdge(method.CppName, call.FunctionName);
                        }
                    }
                }
            }
        }
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
    /// Stub file: fallback implementations for methods called but not compiled.
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
    public GeneratedFile? StubReportFile { get; set; }

    /// <summary>
    /// Write all generated files to a directory.
    /// </summary>
    public void WriteToDirectory(string outputDir)
    {
        Directory.CreateDirectory(outputDir);
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
        if (StubReportFile != null)
        {
            File.WriteAllText(Path.Combine(outputDir, StubReportFile.FileName), StubReportFile.Content);
        }
    }
}

public record StubbedMethodInfo(string TypeName, string MethodName, string CppName, string Reason);

public class GeneratedFile
{
    public string FileName { get; set; } = "";
    public string Content { get; set; } = "";
}
