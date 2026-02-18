using CIL2CPP.Core;
using CIL2CPP.Core.IL;
using CIL2CPP.Core.IR;
using Xunit;

namespace CIL2CPP.Tests.Fixtures;

// ===== HelloWorld Fixture (delegates to static cache) =====

public class HelloWorldFixture
{
    public string DllPath => TestProjectBuilder.HelloWorldDll.Value;

    public IRModule GetReleaseModule() => TestProjectBuilder.HelloWorldReleaseModule.Value;
    public IRModule GetDebugModule() => TestProjectBuilder.HelloWorldDebugModule.Value;
}

// ===== FeatureTest Fixture (delegates to static cache — shared by multiple collections) =====

public class FeatureTestFixture
{
    public string DllPath => TestProjectBuilder.FeatureTestDll.Value;

    public IRModule GetReleaseModule() => TestProjectBuilder.FeatureTestReleaseModule.Value;
    public IRModule GetDebugModule() => TestProjectBuilder.FeatureTestDebugModule.Value;
}

// ===== ArrayTest Fixture =====

public class ArrayTestFixture
{
    public string DllPath => TestProjectBuilder.ArrayTestDll.Value;

    public IRModule GetReleaseModule() => TestProjectBuilder.ArrayTestReleaseModule.Value;
}

// ===== MultiAssembly Fixture =====

public class MultiAssemblyFixture
{
    public string MultiAssemblyTestDllPath => TestProjectBuilder.MultiAssemblyTestDll.Value;
    public string MathLibDllPath => TestProjectBuilder.MathLibDll.Value;

    public IRModule GetMultiAssemblyReleaseModule() => TestProjectBuilder.MultiAssemblyReleaseModule.Value;

    public (AssemblySet Set, ReachabilityResult Reach) GetMathLibReleaseContext()
        => TestProjectBuilder.MathLibReachability.Value;
}

// ===== DllPaths Fixture (lightweight — just paths, no IRModule) =====

public class DllPathsFixture
{
    public string HelloWorldDllPath => TestProjectBuilder.HelloWorldDll.Value;
    public string ArrayTestDllPath => TestProjectBuilder.ArrayTestDll.Value;
    public string FeatureTestDllPath => TestProjectBuilder.FeatureTestDll.Value;
    public string MultiAssemblyTestDllPath => TestProjectBuilder.MultiAssemblyTestDll.Value;
    public string MathLibDllPath => TestProjectBuilder.MathLibDll.Value;
    public string SolutionRoot => TestProjectBuilder.SolutionRoot;
}

// ===== Reachability Fixture (delegates to static cache — AssemblySet kept alive for process lifetime) =====

public class ReachabilityFixture
{
    public string HelloWorldDllPath => TestProjectBuilder.HelloWorldDll.Value;
    public string MultiAssemblyTestDllPath => TestProjectBuilder.MultiAssemblyTestDll.Value;

    public (AssemblySet Set, ReachabilityResult Reach) GetHelloWorldReleaseContext()
        => TestProjectBuilder.HelloWorldReachability.Value;

    public (AssemblySet Set, ReachabilityResult Reach) GetArrayTestReleaseContext()
        => TestProjectBuilder.ArrayTestReachability.Value;

    public (AssemblySet Set, ReachabilityResult Reach) GetFeatureTestReleaseContext()
        => TestProjectBuilder.FeatureTestReachability.Value;

    public (AssemblySet Set, ReachabilityResult Reach) GetMultiAssemblyTestReleaseContext()
        => TestProjectBuilder.MultiAssemblyReachability.Value;

    public (AssemblySet Set, ReachabilityResult Reach) GetMathLibReleaseContext()
        => TestProjectBuilder.MathLibReachability.Value;
}

// ===== Collection Definitions =====

[CollectionDefinition("HelloWorld")]
public class HelloWorldCollection : ICollectionFixture<HelloWorldFixture> { }

// FeatureTest: 4 parallel sub-collections sharing the same static IRModule cache
[CollectionDefinition("FeatureTest")]
public class FeatureTestCollection : ICollectionFixture<FeatureTestFixture> { }

[CollectionDefinition("FeatureTest2")]
public class FeatureTest2Collection : ICollectionFixture<FeatureTestFixture> { }

[CollectionDefinition("FeatureTest3")]
public class FeatureTest3Collection : ICollectionFixture<FeatureTestFixture> { }

[CollectionDefinition("FeatureTest4")]
public class FeatureTest4Collection : ICollectionFixture<FeatureTestFixture> { }

[CollectionDefinition("ArrayTest")]
public class ArrayTestCollection : ICollectionFixture<ArrayTestFixture> { }

[CollectionDefinition("MultiAssembly")]
public class MultiAssemblyCollection : ICollectionFixture<MultiAssemblyFixture> { }

[CollectionDefinition("DllPaths")]
public class DllPathsCollection : ICollectionFixture<DllPathsFixture> { }

[CollectionDefinition("Reachability")]
public class ReachabilityCollection : ICollectionFixture<ReachabilityFixture> { }
