namespace CIL2CPP.Core.IR;

/// <summary>
/// Resolves ILLink feature switches to compile-time constants for AOT compilation.
/// Feature switches are static readonly bool fields in the BCL that ILLink replaces
/// at link time. In AOT mode, most dynamic features are disabled.
///
/// When a feature switch is resolved, the IRBuilder substitutes the Ldsfld instruction
/// with a constant, enabling dead-code elimination of guarded paths.
/// </summary>
public class FeatureSwitchResolver
{
    /// <summary>
    /// Default AOT feature switch values. These match .NET NativeAOT linker defaults.
    /// Key format: "DeclaringType.FullName::FieldName"
    /// </summary>
    private static readonly Dictionary<string, bool> AotDefaults = new()
    {
        // RuntimeFeature — AOT does not support dynamic code generation
        ["System.Runtime.CompilerServices.RuntimeFeature::IsDynamicCodeSupported"] = false,
        ["System.Runtime.CompilerServices.RuntimeFeature::IsDynamicCodeCompiled"] = false,

        // Debugger — debugger attach not supported in AOT
        ["System.Diagnostics.Debugger::IsSupported"] = false,

        // EventSource — CLR event tracing not available in AOT
        ["System.Diagnostics.Tracing.EventSource::IsSupported"] = false,

        // Reflection.Emit — fundamentally incompatible with AOT
        ["Internal.Runtime.InteropServices.ComponentActivator::IsSupported"] = false,

        // BinaryFormatter — security risk, disabled by default since .NET 8
        ["System.Runtime.Serialization.EnableUnsafeBinaryFormatterSerialization"] = false,

        // StackTrace — CIL2CPP has its own stack trace; BCL StackFrame is CLR-internal
        ["System.Diagnostics.StackTrace::IsSupported"] = false,

        // Globalization — not invariant mode by default (ICU is integrated)
        ["System.Globalization.GlobalizationMode::Invariant"] = false,

        // AutoreleasePool — Apple platforms only
        ["System.Threading.Thread::EnableAutoreleasePool"] = false,

        // Metrics — CLR runtime metrics infrastructure
        ["System.Diagnostics.Metrics.RuntimeMetrics::IsEnabled"] = false,
    };

    private readonly Dictionary<string, bool> _overrides;

    /// <summary>
    /// Create a resolver with optional user overrides.
    /// Overrides take precedence over AOT defaults.
    /// </summary>
    public FeatureSwitchResolver(Dictionary<string, bool>? overrides = null)
    {
        _overrides = overrides ?? new();
    }

    /// <summary>
    /// Try to resolve a static field to a compile-time constant.
    /// </summary>
    /// <param name="declaringTypeFullName">Full IL name of the declaring type</param>
    /// <param name="fieldName">Field name</param>
    /// <param name="value">The resolved constant value</param>
    /// <returns>True if this field is a known feature switch</returns>
    public bool TryResolve(string declaringTypeFullName, string fieldName, out bool value)
    {
        var key = $"{declaringTypeFullName}::{fieldName}";

        // User overrides take precedence
        if (_overrides.TryGetValue(key, out value))
            return true;

        // Built-in AOT defaults
        if (AotDefaults.TryGetValue(key, out value))
            return true;

        value = false;
        return false;
    }

    /// <summary>
    /// Get all known feature switch keys (for diagnostics/logging).
    /// </summary>
    public IEnumerable<string> GetKnownSwitches()
    {
        return AotDefaults.Keys.Concat(_overrides.Keys).Distinct();
    }
}
