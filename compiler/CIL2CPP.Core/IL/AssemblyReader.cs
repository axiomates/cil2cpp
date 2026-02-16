using Mono.Cecil;
using CIL2CPP.Core;

namespace CIL2CPP.Core.IL;

/// <summary>
/// Reads .NET assemblies using Mono.Cecil and extracts type information.
/// </summary>
public class AssemblyReader : IDisposable
{
    private readonly AssemblyDefinition _assembly;
    private readonly ModuleDefinition _mainModule;

    public string AssemblyName => _assembly.Name.Name;
    public AssemblyDefinition Assembly => _assembly;

    /// <summary>Whether debug symbols were successfully loaded.</summary>
    public bool HasSymbols { get; }

    public AssemblyReader(string assemblyPath, BuildConfiguration? config = null)
    {
        var readSymbols = config?.ReadDebugSymbols ?? false;

        // Create a resolver that probes the assembly's directory
        var resolver = new CIL2CPPAssemblyResolver();
        var assemblyDir = Path.GetDirectoryName(Path.GetFullPath(assemblyPath));
        if (assemblyDir != null)
            resolver.AddSearchDirectory(assemblyDir);

        // Add .NET runtime directory for BCL resolution
        var runtimeDir = RuntimeLocator.FindRuntimeDirectory(assemblyPath);
        if (runtimeDir != null)
            resolver.AddSearchDirectory(runtimeDir);

        if (readSymbols)
        {
            try
            {
                var readerParams = new ReaderParameters
                {
                    ReadSymbols = true,
                    ReadWrite = false,
                    AssemblyResolver = resolver
                };
                _assembly = AssemblyDefinition.ReadAssembly(assemblyPath, readerParams);
                _mainModule = _assembly.MainModule;
                HasSymbols = true;
                resolver.RegisterAssembly(_assembly);
                return;
            }
            catch
            {
                // PDB/MDB not found or other symbol-reading error, fall through
            }
        }

        // Fallback: read without symbols
        var fallbackParams = new ReaderParameters
        {
            ReadSymbols = false,
            ReadWrite = false,
            AssemblyResolver = resolver
        };
        _assembly = AssemblyDefinition.ReadAssembly(assemblyPath, fallbackParams);
        _mainModule = _assembly.MainModule;
        HasSymbols = false;
        resolver.RegisterAssembly(_assembly);
    }

    /// <summary>
    /// Gets all type definitions from the assembly, including nested types.
    /// </summary>
    public IEnumerable<TypeDefinitionInfo> GetAllTypes()
    {
        foreach (var type in _mainModule.Types)
        {
            // Skip the special <Module> type
            if (type.Name == "<Module>")
                continue;

            yield return new TypeDefinitionInfo(type);

            // Process nested types recursively
            foreach (var nested in GetNestedTypes(type))
            {
                yield return nested;
            }
        }
    }

    private IEnumerable<TypeDefinitionInfo> GetNestedTypes(TypeDefinition type)
    {
        foreach (var nested in type.NestedTypes)
        {
            yield return new TypeDefinitionInfo(nested);

            foreach (var deepNested in GetNestedTypes(nested))
            {
                yield return deepNested;
            }
        }
    }

    /// <summary>
    /// Gets a specific type by full name.
    /// </summary>
    public TypeDefinitionInfo? GetType(string fullName)
    {
        var type = _mainModule.GetType(fullName);
        return type != null ? new TypeDefinitionInfo(type) : null;
    }

    /// <summary>
    /// Gets all referenced assemblies.
    /// </summary>
    public IEnumerable<string> GetReferencedAssemblies()
    {
        return _mainModule.AssemblyReferences.Select(r => r.Name);
    }

    public void Dispose()
    {
        _assembly.Dispose();
    }
}
