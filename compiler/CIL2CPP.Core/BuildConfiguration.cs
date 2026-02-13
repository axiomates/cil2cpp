namespace CIL2CPP.Core;

/// <summary>
/// Build configuration controlling Debug/Release behavior across the pipeline.
/// </summary>
public record BuildConfiguration
{
    /// <summary>Whether this is a Debug build.</summary>
    public bool IsDebug { get; init; }

    /// <summary>Emit #line directives mapping back to C# source files.</summary>
    public bool EmitLineDirectives { get; init; }

    /// <summary>Emit IL offset comments (/* IL_0000 */) in generated C++.</summary>
    public bool EmitILOffsetComments { get; init; }

    /// <summary>Enable runtime stack trace capture.</summary>
    public bool EnableStackTraces { get; init; }

    /// <summary>Read debug symbols (PDB/MDB) from the input assembly.</summary>
    public bool ReadDebugSymbols { get; init; }

    /// <summary>Configuration name for CMake (Debug or Release).</summary>
    public string ConfigurationName => IsDebug ? "Debug" : "Release";

    /// <summary>Pre-configured Debug build settings.</summary>
    public static BuildConfiguration Debug => new()
    {
        IsDebug = true,
        EmitLineDirectives = true,
        EmitILOffsetComments = true,
        EnableStackTraces = true,
        ReadDebugSymbols = true,
    };

    /// <summary>Pre-configured Release build settings.</summary>
    public static BuildConfiguration Release => new()
    {
        IsDebug = false,
        EmitLineDirectives = false,
        EmitILOffsetComments = false,
        EnableStackTraces = false,
        ReadDebugSymbols = false,
    };

    /// <summary>Create configuration from a string name.</summary>
    public static BuildConfiguration FromName(string name) => name.ToLowerInvariant() switch
    {
        "debug" => Debug,
        "release" => Release,
        _ => throw new ArgumentException($"Unknown configuration: {name}. Use 'Debug' or 'Release'.")
    };
}
