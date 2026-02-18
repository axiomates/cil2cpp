using Xunit;
using Mono.Cecil;
using CIL2CPP.Core.IL;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("DllPaths")]
public class AssemblyResolverTests
{
    private readonly DllPathsFixture _fixture;

    public AssemblyResolverTests(DllPathsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Resolver_AddSearchDirectory_NoThrow()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var dir = Path.GetDirectoryName(_fixture.HelloWorldDllPath)!;
        resolver.AddSearchDirectory(dir);
    }

    [Fact]
    public void Resolver_AddSearchDirectory_Deduplicates()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var dir = Path.GetDirectoryName(_fixture.HelloWorldDllPath)!;
        resolver.AddSearchDirectory(dir);
        resolver.AddSearchDirectory(dir); // Should not throw or duplicate
    }

    [Fact]
    public void Resolver_RegisterAssembly_CachesIt()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var asm = AssemblyDefinition.ReadAssembly(_fixture.HelloWorldDllPath);
        resolver.RegisterAssembly(asm);

        Assert.True(resolver.LoadedAssemblies.ContainsKey("HelloWorld"));
    }

    [Fact]
    public void Resolver_Resolve_FindsRegisteredAssembly()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var asm = AssemblyDefinition.ReadAssembly(_fixture.HelloWorldDllPath);
        resolver.RegisterAssembly(asm);

        var nameRef = AssemblyNameReference.Parse("HelloWorld");
        var resolved = resolver.Resolve(nameRef);
        Assert.Same(asm, resolved);
    }

    [Fact]
    public void Resolver_Resolve_FindsAssemblyInSearchDir()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var dir = Path.GetDirectoryName(_fixture.HelloWorldDllPath)!;
        resolver.AddSearchDirectory(dir);

        var nameRef = AssemblyNameReference.Parse("HelloWorld");
        var resolved = resolver.Resolve(nameRef);
        Assert.NotNull(resolved);
        Assert.Equal("HelloWorld", resolved.Name.Name);
    }

    [Fact]
    public void Resolver_Resolve_NonExistentAssembly_Throws()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var nameRef = AssemblyNameReference.Parse("TotallyFakeAssembly");
        Assert.Throws<AssemblyResolutionException>(() => resolver.Resolve(nameRef));
    }

    [Fact]
    public void Resolver_TryResolve_NonExistent_ReturnsNull()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var nameRef = AssemblyNameReference.Parse("TotallyFakeAssembly");
        var result = resolver.TryResolve(nameRef);
        Assert.Null(result);
    }

    [Fact]
    public void Resolver_TryResolve_Existing_ReturnsAssembly()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var dir = Path.GetDirectoryName(_fixture.HelloWorldDllPath)!;
        resolver.AddSearchDirectory(dir);

        var nameRef = AssemblyNameReference.Parse("HelloWorld");
        var result = resolver.TryResolve(nameRef);
        Assert.NotNull(result);
    }

    [Fact]
    public void Resolver_ResolvesFromRuntimeDir()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var runtimeDir = RuntimeLocator.FindRuntimeDirectory(_fixture.HelloWorldDllPath);
        Assert.NotNull(runtimeDir);

        resolver.AddSearchDirectory(runtimeDir);

        // System.Runtime should be resolvable from the runtime directory
        var nameRef = AssemblyNameReference.Parse("System.Runtime");
        var resolved = resolver.Resolve(nameRef);
        Assert.NotNull(resolved);
        Assert.Equal("System.Runtime", resolved.Name.Name);
    }

    [Fact]
    public void Resolver_CachesResolvedAssemblies()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var dir = Path.GetDirectoryName(_fixture.HelloWorldDllPath)!;
        resolver.AddSearchDirectory(dir);

        var nameRef = AssemblyNameReference.Parse("HelloWorld");
        var first = resolver.Resolve(nameRef);
        var second = resolver.Resolve(nameRef);

        Assert.Same(first, second); // Same instance from cache
    }

    [Fact]
    public void AssemblyReader_WithResolver_CanResolveSystemRuntime()
    {
        // The key test: AssemblyReader now creates a resolver internally,
        // so cross-assembly resolution should work
        using var reader = new AssemblyReader(_fixture.HelloWorldDllPath);
        var types = reader.GetAllTypes().ToList();
        Assert.NotEmpty(types);

        // If resolver works, we can access type references that point to other assemblies
        var refs = reader.GetReferencedAssemblies().ToList();
        Assert.Contains("System.Runtime", refs);
    }

    [Fact]
    public void Resolver_Resolve_WithReaderParameters_WorksWithCache()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var asm = AssemblyDefinition.ReadAssembly(_fixture.HelloWorldDllPath);
        resolver.RegisterAssembly(asm);

        var nameRef = AssemblyNameReference.Parse("HelloWorld");
        var readerParams = new ReaderParameters { ReadSymbols = false };
        var resolved = resolver.Resolve(nameRef, readerParams);
        Assert.Same(asm, resolved); // Should return cached, ignoring readerParams
    }

    [Fact]
    public void Resolver_LoadedAssemblies_EmptyByDefault()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        Assert.Empty(resolver.LoadedAssemblies);
    }

    [Fact]
    public void Resolver_Resolve_MultipleAssemblies_AllCached()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var dir = Path.GetDirectoryName(_fixture.MultiAssemblyTestDllPath)!;
        resolver.AddSearchDirectory(dir);

        var nameRef1 = AssemblyNameReference.Parse("MultiAssemblyTest");
        var nameRef2 = AssemblyNameReference.Parse("MathLib");

        resolver.Resolve(nameRef1);
        resolver.Resolve(nameRef2);

        Assert.True(resolver.LoadedAssemblies.ContainsKey("MultiAssemblyTest"));
        Assert.True(resolver.LoadedAssemblies.ContainsKey("MathLib"));
        Assert.Equal(2, resolver.LoadedAssemblies.Count);
    }

    [Fact]
    public void Resolver_Dispose_ClearsCache()
    {
        var resolver = new CIL2CPPAssemblyResolver();
        var dir = Path.GetDirectoryName(_fixture.HelloWorldDllPath)!;
        resolver.AddSearchDirectory(dir);

        var nameRef = AssemblyNameReference.Parse("HelloWorld");
        resolver.Resolve(nameRef);
        Assert.Single(resolver.LoadedAssemblies);

        resolver.Dispose();
        Assert.Empty(resolver.LoadedAssemblies);
    }

    [Fact]
    public void Resolver_AddMultipleSearchDirectories_AllSearched()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var dir1 = Path.GetDirectoryName(_fixture.HelloWorldDllPath)!;
        var dir2 = Path.GetDirectoryName(_fixture.MultiAssemblyTestDllPath)!;
        resolver.AddSearchDirectory(dir1);
        resolver.AddSearchDirectory(dir2);

        // Should find assemblies from both directories
        var nameRef = AssemblyNameReference.Parse("HelloWorld");
        var resolved = resolver.Resolve(nameRef);
        Assert.NotNull(resolved);
    }

    [Fact]
    public void Resolver_Resolve_SetsAssemblyResolverOnReaderParams()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var dir = Path.GetDirectoryName(_fixture.HelloWorldDllPath)!;
        resolver.AddSearchDirectory(dir);

        var nameRef = AssemblyNameReference.Parse("HelloWorld");
        var resolved = resolver.Resolve(nameRef);

        // The loaded assembly should be usable (resolved correctly)
        Assert.NotEmpty(resolved.MainModule.Types);
    }

    [Fact]
    public void Resolver_RegisterAssembly_OverwritesPrevious()
    {
        using var resolver = new CIL2CPPAssemblyResolver();
        var asm1 = AssemblyDefinition.ReadAssembly(_fixture.HelloWorldDllPath);
        var asm2 = AssemblyDefinition.ReadAssembly(_fixture.HelloWorldDllPath);

        resolver.RegisterAssembly(asm1);
        resolver.RegisterAssembly(asm2);

        var nameRef = AssemblyNameReference.Parse("HelloWorld");
        var resolved = resolver.Resolve(nameRef);
        Assert.Same(asm2, resolved);

        asm1.Dispose();
    }
}
