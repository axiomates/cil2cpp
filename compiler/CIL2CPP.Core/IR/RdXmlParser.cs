using System.Xml.Linq;

namespace CIL2CPP.Core.IR;

/// <summary>
/// Parses rd.xml (runtime directives) files for type/member preservation rules.
/// These files are shipped with NuGet packages to prevent tree-shaking of
/// types that are accessed via reflection or serialization.
/// Format: https://learn.microsoft.com/en-us/windows/uwp/dotnet-native/runtime-directives-rd-xml-configuration-file-reference
/// </summary>
public static class RdXmlParser
{
    /// <summary>
    /// A single preservation rule: keep specified members of a type alive.
    /// </summary>
    public record PreservationRule(
        string? AssemblyName,
        string? TypeName,
        string? MethodName,
        int MemberTypes);

    /// <summary>
    /// Parse an rd.xml file and return preservation rules.
    /// </summary>
    public static List<PreservationRule> Parse(string xmlPath)
    {
        var rules = new List<PreservationRule>();

        if (!File.Exists(xmlPath))
            return rules;

        var doc = XDocument.Load(xmlPath);
        var root = doc.Root;
        if (root == null) return rules;

        // Handle both with and without namespace
        var ns = root.GetDefaultNamespace();

        foreach (var app in root.Descendants(ns + "Application"))
        {
            foreach (var asm in app.Elements(ns + "Assembly"))
            {
                var asmName = asm.Attribute("Name")?.Value;

                // Assembly-level preservation (no specific type)
                var asmDynamic = asm.Attribute("Dynamic")?.Value;
                if (asmDynamic != null && asmName != null)
                {
                    rules.Add(new PreservationRule(asmName, null, null,
                        MapDynamicToMemberTypes(asmDynamic)));
                }

                foreach (var type in asm.Elements(ns + "Type"))
                {
                    var typeName = type.Attribute("Name")?.Value;
                    if (typeName == null) continue;

                    var dynamic = type.Attribute("Dynamic")?.Value;
                    var browse = type.Attribute("Browse")?.Value;
                    var serialize = type.Attribute("Serialize")?.Value;

                    // Use the most inclusive directive
                    var directive = dynamic ?? browse ?? serialize;
                    var memberTypes = directive != null
                        ? MapDynamicToMemberTypes(directive)
                        : -1; // default: preserve all

                    rules.Add(new PreservationRule(asmName, typeName, null, memberTypes));

                    // Method-level rules
                    foreach (var method in type.Elements(ns + "Method"))
                    {
                        var methodName = method.Attribute("Name")?.Value;
                        if (methodName != null)
                            rules.Add(new PreservationRule(asmName, typeName, methodName, -1));
                    }

                    // Property-level rules → seed get_/set_ methods
                    foreach (var prop in type.Elements(ns + "Property"))
                    {
                        var propName = prop.Attribute("Name")?.Value;
                        if (propName != null)
                        {
                            rules.Add(new PreservationRule(asmName, typeName, $"get_{propName}", -1));
                            rules.Add(new PreservationRule(asmName, typeName, $"set_{propName}", -1));
                        }
                    }
                }
            }
        }

        return rules;
    }

    /// <summary>
    /// Map rd.xml "Dynamic"/"Browse" attribute values to DynamicallyAccessedMemberTypes flags.
    /// </summary>
    private static int MapDynamicToMemberTypes(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "required all" or "all" => -1,                          // All members
            "required public" or "public" => 0x2FE3,               // All public members
            "required publicandinternal" => -1,                     // Treat as All
            "auto" => -1,                                           // Let the analyzer decide
            _ => -1,                                                // Default: preserve all
        };
    }
}
