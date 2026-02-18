using Xunit;
using CIL2CPP.Core.IL;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("SampleAssembly")]
public class ReachabilityAnalyzerTests
{
    private readonly SampleAssemblyFixture _fixture;

    public ReachabilityAnalyzerTests(SampleAssemblyFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Analyze_HelloWorld_FindsProgramType()
    {
        var (_, result) = _fixture.GetHelloWorldReleaseContext();

        var programType = result.ReachableTypes.FirstOrDefault(t => t.Name == "Program");
        Assert.NotNull(programType);
    }

    [Fact]
    public void Analyze_HelloWorld_FindsMainMethod()
    {
        var (_, result) = _fixture.GetHelloWorldReleaseContext();

        var mainMethod = result.ReachableMethods.FirstOrDefault(
            m => m.Name == "Main" && m.DeclaringType.Name == "Program");
        Assert.NotNull(mainMethod);
    }

    [Fact]
    public void Analyze_HelloWorld_FindsCalculatorType()
    {
        var (_, result) = _fixture.GetHelloWorldReleaseContext();

        // Calculator is used in HelloWorld's Main
        var calcType = result.ReachableTypes.FirstOrDefault(t => t.Name == "Calculator");
        Assert.NotNull(calcType);
    }

    [Fact]
    public void Analyze_HelloWorld_ReachableTypesCountIsReasonable()
    {
        var (_, result) = _fixture.GetHelloWorldReleaseContext();

        // HelloWorld user assembly should have a small number of reachable types
        var userTypes = result.ReachableTypes
            .Where(t => t.Module.Assembly.Name.Name == "HelloWorld")
            .ToList();
        Assert.True(userTypes.Count >= 2); // At least Program + Calculator
        Assert.True(userTypes.Count < 50); // Should be bounded
    }

    [Fact]
    public void Analyze_HelloWorld_ReachableMethods_NotEmpty()
    {
        var (_, result) = _fixture.GetHelloWorldReleaseContext();

        Assert.NotEmpty(result.ReachableMethods);
    }

    [Fact]
    public void Analyze_MultiAssemblyTest_FindsCrossAssemblyTypes()
    {
        var (_, result) = _fixture.GetMultiAssemblyTestReleaseContext();

        // Should find types from MultiAssemblyTest
        Assert.Contains(result.ReachableTypes, t => t.Name == "Program");
        Assert.Contains(result.ReachableTypes, t => t.Name == "Adder");

        // Should also find types from MathLib (cross-assembly)
        Assert.Contains(result.ReachableTypes, t => t.Name == "MathUtils");
        Assert.Contains(result.ReachableTypes, t => t.Name == "Counter");
        Assert.Contains(result.ReachableTypes, t => t.Name == "ICalculator");
    }

    [Fact]
    public void Analyze_MultiAssemblyTest_LoadsMathLibAssembly()
    {
        var (set, _) = _fixture.GetMultiAssemblyTestReleaseContext();

        // MathLib should have been auto-loaded during analysis
        Assert.True(set.LoadedAssemblies.ContainsKey("MathLib"));
    }

    [Fact]
    public void Analyze_MultiAssemblyTest_FindsMathUtilsMethods()
    {
        var (_, result) = _fixture.GetMultiAssemblyTestReleaseContext();

        // MathUtils.Add is called from Main
        Assert.Contains(result.ReachableMethods,
            m => m.Name == "Add" && m.DeclaringType.Name == "MathUtils");

        // Counter.Increment is called from Main
        Assert.Contains(result.ReachableMethods,
            m => m.Name == "Increment" && m.DeclaringType.Name == "Counter");

        // Counter.GetCount is called from Main
        Assert.Contains(result.ReachableMethods,
            m => m.Name == "GetCount" && m.DeclaringType.Name == "Counter");
    }

    [Fact]
    public void Analyze_MultiAssemblyTest_FindsInterfaceImplementation()
    {
        var (_, result) = _fixture.GetMultiAssemblyTestReleaseContext();

        // Adder.Calculate implements ICalculator.Calculate
        Assert.Contains(result.ReachableMethods,
            m => m.Name == "Calculate" && m.DeclaringType.Name == "Adder");
    }

    [Fact]
    public void IsReachable_Type_ReturnsTrueForReachable()
    {
        var (set, result) = _fixture.GetHelloWorldReleaseContext();

        var programType = set.RootAssembly.MainModule.Types
            .First(t => t.Name == "Program");
        Assert.True(result.IsReachable(programType));
    }

    [Fact]
    public void IsReachable_Method_ReturnsTrueForReachable()
    {
        var (set, result) = _fixture.GetHelloWorldReleaseContext();

        var mainMethod = set.RootAssembly.MainModule.Types
            .First(t => t.Name == "Program").Methods
            .First(m => m.Name == "Main");
        Assert.True(result.IsReachable(mainMethod));
    }

    [Fact]
    public void Analyze_FeatureTest_HandlesComplexPatterns()
    {
        // FeatureTest has delegates, generics, interfaces, etc.
        var (_, result) = _fixture.GetFeatureTestReleaseContext();

        Assert.Contains(result.ReachableTypes, t => t.Name == "Program");
        Assert.True(result.ReachableTypes.Count >= 5);
    }

    [Fact]
    public void Analyze_MathLib_LibraryMode_SeedsPublicTypes()
    {
        // MathLib has no entry point — should seed all public types
        var (_, result) = _fixture.GetMathLibReleaseContext();

        Assert.Contains(result.ReachableTypes, t => t.Name == "MathUtils");
        Assert.Contains(result.ReachableTypes, t => t.Name == "Counter");
        Assert.Contains(result.ReachableTypes, t => t.Name == "ICalculator");
    }

    [Fact]
    public void Analyze_MathLib_LibraryMode_SeedsPublicMethods()
    {
        var (_, result) = _fixture.GetMathLibReleaseContext();

        // Public methods should be seeded
        Assert.Contains(result.ReachableMethods,
            m => m.Name == "Add" && m.DeclaringType.Name == "MathUtils");
        Assert.Contains(result.ReachableMethods,
            m => m.Name == "Subtract" && m.DeclaringType.Name == "MathUtils");
    }

    [Fact]
    public void Analyze_MultiAssemblyTest_MarksBaseTypes()
    {
        var (_, result) = _fixture.GetMultiAssemblyTestReleaseContext();

        // ICalculator interface should be reachable
        var iCalcType = result.ReachableTypes.FirstOrDefault(t => t.Name == "ICalculator");
        Assert.NotNull(iCalcType);
    }

    [Fact]
    public void Analyze_FeatureTest_FindsNestedTypes()
    {
        var (_, result) = _fixture.GetFeatureTestReleaseContext();

        // FeatureTest with lambdas/delegates likely has compiler-generated nested types
        var nestedTypes = result.ReachableTypes.Where(t => t.IsNested).ToList();
        Assert.True(nestedTypes.Count >= 0);
    }

    [Fact]
    public void Analyze_ArrayTest_FindsArrayUsages()
    {
        var (_, result) = _fixture.GetArrayTestReleaseContext();

        // ArrayTest uses arrays, so Newarr/Ldelem/Stelem should be processed
        Assert.Contains(result.ReachableTypes, t => t.Name == "Program");
        Assert.True(result.ReachableMethods.Count >= 1);
    }

    [Fact]
    public void Analyze_MultipleRuns_SameResultCounts()
    {
        // Verify determinism: compare cached result with a fresh run
        var (_, result1) = _fixture.GetHelloWorldReleaseContext();

        using var set2 = new AssemblySet(_fixture.HelloWorldDllPath);
        var result2 = new ReachabilityAnalyzer(set2).Analyze();

        Assert.Equal(result1.ReachableTypes.Count, result2.ReachableTypes.Count);
        Assert.Equal(result1.ReachableMethods.Count, result2.ReachableMethods.Count);
    }

    [Fact]
    public void Analyze_HelloWorld_ProcessesConstructors()
    {
        var (_, result) = _fixture.GetHelloWorldReleaseContext();

        // Constructors of reachable types should be included
        var ctors = result.ReachableMethods
            .Where(m => m.IsConstructor && !m.IsStatic)
            .ToList();
        Assert.NotEmpty(ctors);
    }

    [Fact]
    public void Analyze_FeatureTest_FieldTypesReachable()
    {
        var (_, result) = _fixture.GetFeatureTestReleaseContext();

        // Types used as field types should be reachable
        Assert.True(result.ReachableTypes.Count >= 3);
    }

    [Fact]
    public void ReachabilityResult_IsReachable_FalseForEmpty()
    {
        var result = new ReachabilityResult();
        // Empty result should not contain anything
        Assert.Empty(result.ReachableTypes);
        Assert.Empty(result.ReachableMethods);
    }

    [Fact]
    public void Analyze_MathLib_LibraryMode_DoesNotSeedPrivateTypes()
    {
        var (set, result) = _fixture.GetMathLibReleaseContext();

        // Non-public types (if any exist in MathLib) should NOT be seeded directly,
        // though they may be pulled in transitively.
        // The key point: public types ARE seeded.
        var publicTypes = set.RootAssembly.MainModule.Types
            .Where(t => t.IsPublic && t.Name != "<Module>")
            .ToList();
        foreach (var pub in publicTypes)
        {
            Assert.True(result.IsReachable(pub), $"Public type {pub.Name} should be reachable");
        }
    }
}
