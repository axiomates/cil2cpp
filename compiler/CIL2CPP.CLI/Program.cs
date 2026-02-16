using System.CommandLine;
using System.Diagnostics;
using CIL2CPP.Core;
using CIL2CPP.Core.IL;
using CIL2CPP.Core.IR;
using CIL2CPP.Core.CodeGen;

namespace CIL2CPP.CLI;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("CIL2CPP - .NET to C++ AOT Compiler");

        // compile command
        var inputOption = new Option<FileInfo>(
            name: "--input",
            description: "Input C# project file (.csproj)")
        { IsRequired = true };
        inputOption.AddAlias("-i");

        var outputOption = new Option<DirectoryInfo>(
            name: "--output",
            description: "Output directory for generated C++ code")
        { IsRequired = true };
        outputOption.AddAlias("-o");

        var configOption = new Option<string>(
            name: "--configuration",
            getDefaultValue: () => "Release",
            description: "Build configuration (Debug or Release)");
        configOption.AddAlias("-c");

        var compileCommand = new Command("compile", "Compile C# project to native executable")
        {
            inputOption,
            outputOption,
            configOption
        };

        compileCommand.SetHandler((input, output, config) =>
        {
            Compile(input, output, config);
        }, inputOption, outputOption, configOption);

        rootCommand.AddCommand(compileCommand);

        // codegen command - generate C++ only (no native compile)
        var codegenInputOption = new Option<FileInfo>("--input", "Input C# project file (.csproj)") { IsRequired = true };
        codegenInputOption.AddAlias("-i");
        var codegenOutputOption = new Option<DirectoryInfo>("--output", "Output directory") { IsRequired = true };
        codegenOutputOption.AddAlias("-o");
        var codegenConfigOption = new Option<string>(
            name: "--configuration",
            getDefaultValue: () => "Release",
            description: "Build configuration (Debug or Release)");
        codegenConfigOption.AddAlias("-c");

        var codegenCommand = new Command("codegen", "Generate C++ code from C# project (without compiling)")
        {
            codegenInputOption, codegenOutputOption, codegenConfigOption
        };

        codegenCommand.SetHandler((input, output, config) =>
        {
            GenerateCpp(input, output, config);
        }, codegenInputOption, codegenOutputOption, codegenConfigOption);

        rootCommand.AddCommand(codegenCommand);

        // dump command - for debugging IL
        var dumpInputOption = new Option<FileInfo>(
            name: "--input",
            description: "Input C# project file (.csproj)")
        { IsRequired = true };
        dumpInputOption.AddAlias("-i");

        var dumpCommand = new Command("dump", "Dump IL information from assembly")
        {
            dumpInputOption
        };

        dumpCommand.SetHandler((input) =>
        {
            DumpAssembly(input);
        }, dumpInputOption);

        rootCommand.AddCommand(dumpCommand);

        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// Build a .csproj and return the path to the output DLL.
    /// </summary>
    static FileInfo BuildAndResolve(FileInfo input)
    {
        if (!input.Exists)
        {
            throw new FileNotFoundException($"Input file not found: {input.FullName}");
        }

        var ext = input.Extension.ToLowerInvariant();
        if (ext != ".csproj")
        {
            throw new ArgumentException(
                $"Expected .csproj file, got '{ext}'. CIL2CPP accepts C# project files as input.");
        }

        Console.WriteLine($"Building {input.Name}...");

        var process = new Process();
        process.StartInfo.FileName = "dotnet";
        process.StartInfo.Arguments = $"build \"{input.FullName}\" --nologo -v q";
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.Start();

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            var msg = !string.IsNullOrWhiteSpace(stderr) ? stderr : stdout;
            throw new InvalidOperationException($"dotnet build failed:\n{msg.Trim()}");
        }

        Console.WriteLine("Build succeeded.");

        // Find the output DLL by scanning bin/{Debug,Release}/net*/
        var projectDir = input.DirectoryName!;
        var assemblyName = Path.GetFileNameWithoutExtension(input.Name);

        foreach (var config in new[] { "Debug", "Release" })
        {
            var binDir = Path.Combine(projectDir, "bin", config);
            if (!Directory.Exists(binDir)) continue;

            foreach (var tfmDir in Directory.GetDirectories(binDir))
            {
                var candidate = Path.Combine(tfmDir, $"{assemblyName}.dll");
                if (File.Exists(candidate))
                {
                    return new FileInfo(candidate);
                }
            }
        }

        throw new FileNotFoundException(
            $"Could not find output DLL for project {input.Name}. " +
            $"Expected: {Path.Combine(projectDir, "bin", "*", "net*", $"{assemblyName}.dll")}");
    }

    /// <summary>
    /// Common setup: build the project, resolve output DLL, parse build config.
    /// Returns null if setup fails (error already printed).
    /// </summary>
    static (FileInfo AssemblyFile, BuildConfiguration Config)? PrepareBuild(
        FileInfo input, DirectoryInfo output, string configName)
    {
        FileInfo assemblyFile;
        try
        {
            assemblyFile = BuildAndResolve(input);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return null;
        }

        BuildConfiguration config;
        try
        {
            config = BuildConfiguration.FromName(configName);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return null;
        }

        output.Create();
        return (assemblyFile, config);
    }

    static void PrintGeneratedFiles(GeneratedOutput generatedOutput)
    {
        Console.WriteLine($"      {generatedOutput.HeaderFile.FileName}");
        Console.WriteLine($"      {generatedOutput.SourceFile.FileName}");
        if (generatedOutput.MainFile != null)
            Console.WriteLine($"      {generatedOutput.MainFile.FileName}");
        if (generatedOutput.CMakeFile != null)
            Console.WriteLine($"      {generatedOutput.CMakeFile.FileName}");
    }

    static void PrintBanner(FileInfo assemblyFile, DirectoryInfo output, BuildConfiguration config, string? modeSuffix = null)
    {
        var version = typeof(Program).Assembly.GetName().Version;
        var modeLabel = modeSuffix != null ? $" ({modeSuffix})" : "";
        Console.WriteLine($"CIL2CPP Code Generator v{version?.ToString(3) ?? "0.0.0"}{modeLabel}");
        Console.WriteLine($"Input:  {assemblyFile.FullName}");
        Console.WriteLine($"Output: {output.FullName}");
        Console.WriteLine($"Config: {config.ConfigurationName}");
        Console.WriteLine();
    }

    static void GenerateCpp(FileInfo input, DirectoryInfo output, string configName = "Release")
    {
        var prepared = PrepareBuild(input, output, configName);
        if (prepared is not var (assemblyFile, config)) return;

        try
        {
            PrintBanner(assemblyFile, output, config);

            Console.WriteLine("[1/4] Loading assembly set...");
            using var assemblySet = new AssemblySet(assemblyFile.FullName, config);
            Console.WriteLine($"      Root assembly: {assemblySet.RootAssemblyName}");

            Console.WriteLine("[2/4] Analyzing reachability...");
            var analyzer = new ReachabilityAnalyzer(assemblySet);
            var reachability = analyzer.Analyze();
            Console.WriteLine($"      {reachability.ReachableTypes.Count} reachable types");
            Console.WriteLine($"      {reachability.ReachableMethods.Count} reachable methods");
            Console.WriteLine($"      {assemblySet.LoadedAssemblies.Count} assemblies loaded");

            Console.WriteLine("[3/4] Building IR...");
            using var reader = new AssemblyReader(assemblyFile.FullName, config);
            var builder = new IRBuilder(reader, config);
            var module = builder.Build(assemblySet, reachability);
            Console.WriteLine($"      {module.Types.Count} types, {module.GetAllMethods().Count()} methods");
            if (module.EntryPoint != null)
                Console.WriteLine($"      Entry point: {module.EntryPoint.DeclaringType?.ILFullName}.{module.EntryPoint.Name}");
            else
                Console.WriteLine("      No entry point - generating static library");

            Console.WriteLine("[4/4] Generating C++ code...");
            var generator = new CppCodeGenerator(module, config);
            var generatedOutput = generator.Generate();
            generatedOutput.WriteToDirectory(output.FullName);
            PrintGeneratedFiles(generatedOutput);

            Console.WriteLine();
            var outputType = module.EntryPoint != null ? "executable" : "static library";
            Console.WriteLine($"Code generation completed! ({config.ConfigurationName}, {outputType})");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
        }
    }

    static void Compile(FileInfo input, DirectoryInfo output, string configName = "Release")
    {
        var prepared = PrepareBuild(input, output, configName);
        if (prepared is not var (assemblyFile, config)) return;

        try
        {
            PrintBanner(assemblyFile, output, config);

            Console.WriteLine("[1/5] Loading assembly set...");
            using var assemblySet = new AssemblySet(assemblyFile.FullName, config);

            Console.WriteLine("[2/5] Analyzing reachability...");
            var analyzer = new ReachabilityAnalyzer(assemblySet);
            var reachability = analyzer.Analyze();

            Console.WriteLine("[3/5] Building IR...");
            using var reader = new AssemblyReader(assemblyFile.FullName, config);
            var builder = new IRBuilder(reader, config);
            var module = builder.Build(assemblySet, reachability);
            Console.WriteLine($"      {module.Types.Count} types, {module.GetAllMethods().Count()} methods");

            Console.WriteLine("[4/5] Generating C++ code...");
            var generator = new CppCodeGenerator(module, config);
            var generatedOutput = generator.Generate();
            var cppDir = Path.Combine(output.FullName, "cpp");
            generatedOutput.WriteToDirectory(cppDir);

            Console.WriteLine("[5/5] Compiling to native...");
            Console.WriteLine("      (Native compilation not yet integrated)");

            Console.WriteLine();
            Console.WriteLine($"Compilation completed ({config.ConfigurationName} configuration, native compile pending).");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
        }
    }

    static void DumpAssembly(FileInfo input)
    {
        FileInfo assemblyFile;
        try
        {
            assemblyFile = BuildAndResolve(input);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return;
        }

        try
        {
            using var reader = new AssemblyReader(assemblyFile.FullName);

            Console.WriteLine($"Assembly: {reader.AssemblyName}");
            Console.WriteLine(new string('=', 60));
            Console.WriteLine();

            foreach (var type in reader.GetAllTypes())
            {
                Console.WriteLine($"Type: {type.FullName}");

                if (type.BaseTypeName != null)
                {
                    Console.WriteLine($"  Base: {type.BaseTypeName}");
                }

                if (type.Fields.Any())
                {
                    Console.WriteLine("  Fields:");
                    foreach (var field in type.Fields)
                    {
                        Console.WriteLine($"    {field.TypeName} {field.Name}");
                    }
                }

                if (type.Methods.Any())
                {
                    Console.WriteLine("  Methods:");
                    foreach (var method in type.Methods)
                    {
                        var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.TypeName} {p.Name}"));
                        Console.WriteLine($"    {method.ReturnTypeName} {method.Name}({parameters})");
                    }
                }

                Console.WriteLine();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }
}
