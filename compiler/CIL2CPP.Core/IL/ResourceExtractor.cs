using System.Resources;
using Mono.Cecil;

namespace CIL2CPP.Core.IL;

/// <summary>
/// Extracts embedded .NET string resources from Cecil assemblies.
/// Used to generate the SR resource string lookup table at compile time,
/// replacing the hardcoded table in the runtime.
/// </summary>
public static class ResourceExtractor
{
    /// <summary>
    /// Extract all string resources from an assembly's embedded resources.
    /// Parses .NET binary resource format (.resources) using System.Resources.ResourceReader.
    /// </summary>
    public static Dictionary<string, string> ExtractStringResources(AssemblyDefinition assembly)
    {
        var result = new Dictionary<string, string>();
        foreach (var resource in assembly.MainModule.Resources)
        {
            if (resource is not EmbeddedResource embedded)
                continue;
            if (!resource.Name.EndsWith(".resources", StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                using var stream = embedded.GetResourceStream();
                using var reader = new ResourceReader(stream);
                foreach (System.Collections.DictionaryEntry entry in reader)
                {
                    if (entry.Key is string key && entry.Value is string value)
                        result.TryAdd(key, value);
                }
            }
            catch
            {
                // Skip resources that can't be parsed (e.g., non-string resources, corrupt data)
            }
        }
        return result;
    }

    /// <summary>
    /// Extract string resources from all loaded assemblies, filtered to only keys
    /// that appear as string literals in the compiled code.
    /// </summary>
    public static Dictionary<string, string> ExtractReferencedResources(
        AssemblySet assemblySet, HashSet<string> referencedStringLiterals)
    {
        var allResources = new Dictionary<string, string>();

        foreach (var (_, assembly) in assemblySet.LoadedAssemblies)
        {
            var resources = ExtractStringResources(assembly);
            foreach (var (key, value) in resources)
                allResources.TryAdd(key, value);
        }

        // Filter to only keys actually referenced in compiled code
        var filtered = new Dictionary<string, string>();
        foreach (var (key, value) in allResources)
        {
            if (referencedStringLiterals.Contains(key))
                filtered[key] = value;
        }

        return filtered;
    }
}
