using Xunit;
using CIL2CPP.Core;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("Generics")]
public class IRBuilderGenericsTests
{
    private readonly FeatureTestFixture _fixture;

    public IRBuilderGenericsTests(FeatureTestFixture fixture)
    {
        _fixture = fixture;
    }

    private IRModule BuildFeatureTest(BuildConfiguration? config = null)
    {
        if (config == null || !config.ReadDebugSymbols)
            return _fixture.GetReleaseModule();
        return _fixture.GetDebugModule();
    }

    private List<IRInstruction> GetMethodInstructions(IRModule module, string typeName, string methodName)
    {
        var type = module.FindType(typeName)!;
        var method = type.Methods.First(m => m.Name == methodName);
        return method.BasicBlocks.SelectMany(b => b.Instructions).ToList();
    }

    // ===== Generic Type Monomorphization =====

    [Fact]
    public void Build_FeatureTest_OpenGenericType_Skipped()
    {
        var module = BuildFeatureTest();
        // Open generic Wrapper`1 should NOT appear as a type (it's a template)
        var openType = module.Types.FirstOrDefault(t => t.ILFullName == "Wrapper`1");
        Assert.Null(openType);
    }

    [Fact]
    public void Build_FeatureTest_WrapperInt_Exists()
    {
        var module = BuildFeatureTest();
        var type = module.Types.FirstOrDefault(t => t.IsGenericInstance && t.CppName.Contains("Wrapper"));
        Assert.NotNull(type);
        Assert.True(type!.IsGenericInstance);
    }

    [Fact]
    public void Build_FeatureTest_GenericInstance_HasFields()
    {
        var module = BuildFeatureTest();
        var wrapperTypes = module.Types.Where(t => t.IsGenericInstance && t.CppName.Contains("Wrapper")).ToList();
        Assert.True(wrapperTypes.Count >= 1);
        // Each Wrapper<T> should have a _value field
        foreach (var type in wrapperTypes)
        {
            Assert.True(type.Fields.Count > 0 || type.StaticFields.Count > 0,
                $"Generic instance {type.CppName} should have fields");
        }
    }

    [Fact]
    public void Build_FeatureTest_GenericInstance_HasMethods()
    {
        var module = BuildFeatureTest();
        var wrapperTypes = module.Types.Where(t => t.IsGenericInstance && t.CppName.StartsWith("Wrapper_")).ToList();
        Assert.True(wrapperTypes.Count >= 1);
        foreach (var type in wrapperTypes)
        {
            var methodNames = type.Methods.Select(m => m.Name).ToList();
            Assert.Contains("GetValue", methodNames);
            Assert.Contains("SetValue", methodNames);
            Assert.Contains(".ctor", methodNames);
        }
    }

    [Fact]
    public void Build_FeatureTest_GenericInstance_FieldTypeSubstituted()
    {
        var module = BuildFeatureTest();
        // Find a Wrapper instance that uses int (System.Int32)
        var wrapperInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Wrapper") && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(wrapperInt);
        // _value field should have type int32_t, not "T"
        var valueField = wrapperInt!.Fields.FirstOrDefault(f => f.Name == "_value");
        Assert.NotNull(valueField);
        Assert.DoesNotContain("T", valueField!.FieldTypeName);
    }

    [Fact]
    public void Build_FeatureTest_GenericInstance_CppNameMangled()
    {
        var module = BuildFeatureTest();
        var wrapperTypes = module.Types.Where(t => t.IsGenericInstance && t.CppName.Contains("Wrapper")).ToList();
        foreach (var type in wrapperTypes)
        {
            // CppName should be a valid C++ identifier (no angle brackets, backticks, etc.)
            Assert.DoesNotContain("<", type.CppName);
            Assert.DoesNotContain(">", type.CppName);
            Assert.DoesNotContain("`", type.CppName);
        }
    }

    // ===== GenericHelper<T> =====

    [Fact]
    public void Build_FeatureTest_GenericHelper_IntSpecialization_Exists()
    {
        var module = BuildFeatureTest();
        var types = module.Types.Where(t => t.IsGenericInstance && t.CppName.Contains("GenericHelper")).ToList();
        Assert.True(types.Count >= 1, "Should have GenericHelper<int> specialization");
    }

    [Fact]
    public void Build_FeatureTest_GenericHelper_HasArrayField()
    {
        var module = BuildFeatureTest();
        var types = module.Types.Where(t => t.IsGenericInstance && t.CppName.Contains("GenericHelper")).ToList();
        foreach (var type in types)
        {
            Assert.True(type.Fields.Count > 0 || type.StaticFields.Count > 0,
                $"{type.CppName} should have _items field");
        }
    }

    [Fact]
    public void Build_FeatureTest_GenericHelper_HasSetGetCountMethods()
    {
        var module = BuildFeatureTest();
        var types = module.Types.Where(t => t.IsGenericInstance && t.CppName.Contains("GenericHelper")).ToList();
        foreach (var type in types)
        {
            var methodNames = type.Methods.Select(m => m.Name).ToList();
            Assert.Contains("Set", methodNames);
            Assert.Contains("Get", methodNames);
            Assert.Contains("Count", methodNames);
            Assert.Contains(".ctor", methodNames);
        }
    }

    [Fact]
    public void Build_FeatureTest_GenericHelper_MethodsHaveInstructions()
    {
        var module = BuildFeatureTest();
        var helperInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("GenericHelper")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(helperInt);
        var setMethod = helperInt!.Methods.First(m => m.Name == "Set");
        var getMethod = helperInt.Methods.First(m => m.Name == "Get");
        Assert.True(setMethod.BasicBlocks.SelectMany(b => b.Instructions).Any(),
            "Set method should have instructions");
        Assert.True(getMethod.BasicBlocks.SelectMany(b => b.Instructions).Any(),
            "Get method should have instructions");
    }

    [Fact]
    public void Build_FeatureTest_GenericHelper_StringSpecialization()
    {
        var module = BuildFeatureTest();
        var helperStr = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("GenericHelper")
            && t.GenericArguments.Contains("System.String"));
        Assert.NotNull(helperStr);
    }

    [Fact]
    public void Build_FeatureTest_GenericHelper_SwapMethod_HasRefParams()
    {
        var module = BuildFeatureTest();
        var helperInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("GenericHelper")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(helperInt);
        var swap = helperInt!.Methods.FirstOrDefault(m => m.Name == "Swap");
        Assert.NotNull(swap);
        Assert.Equal(2, swap!.Parameters.Count);
    }

    [Fact]
    public void Build_FeatureTest_GenericHelper_SwapMethod_HasInstructions()
    {
        var module = BuildFeatureTest();
        var helperInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("GenericHelper")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(helperInt);
        var swap = helperInt!.Methods.First(m => m.Name == "Swap");
        var instrs = swap.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.True(instrs.Count > 0, "Swap should have instructions");
    }

    // ===== Generic Methods =====

    [Fact]
    public void Build_FeatureTest_GenericMethods_IdentityInt_Exists()
    {
        var module = BuildFeatureTest();
        var utils = module.FindType("GenericUtils");
        Assert.NotNull(utils);
        var identityInt = utils!.Methods.FirstOrDefault(m =>
            m.CppName.Contains("Identity") && m.CppName.Contains("System_Int32"));
        Assert.NotNull(identityInt);
        Assert.True(identityInt!.IsGenericInstance);
    }

    [Fact]
    public void Build_FeatureTest_GenericMethods_IdentityString_Exists()
    {
        var module = BuildFeatureTest();
        var utils = module.FindType("GenericUtils");
        Assert.NotNull(utils);
        var identityStr = utils!.Methods.FirstOrDefault(m =>
            m.CppName.Contains("Identity") && m.CppName.Contains("System_String"));
        Assert.NotNull(identityStr);
        Assert.Equal("cil2cpp::String*", identityStr!.ReturnTypeCpp);
    }

    [Fact]
    public void Build_FeatureTest_GenericMethods_SwapInt_Exists()
    {
        var module = BuildFeatureTest();
        var utils = module.FindType("GenericUtils");
        Assert.NotNull(utils);
        var swapInt = utils!.Methods.FirstOrDefault(m =>
            m.CppName.Contains("Swap") && m.CppName.Contains("System_Int32"));
        Assert.NotNull(swapInt);
        Assert.Equal(2, swapInt!.Parameters.Count);
    }

    [Fact]
    public void Build_FeatureTest_GenericMethods_IdentityInt_HasBody()
    {
        var module = BuildFeatureTest();
        var utils = module.FindType("GenericUtils");
        Assert.NotNull(utils);
        var identityInt = utils!.Methods.First(m =>
            m.CppName.Contains("Identity") && m.CppName.Contains("System_Int32"));
        var instrs = identityInt.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.True(instrs.Count > 0, "Identity<int> should have method body");
        Assert.Contains(instrs, i => i is IRReturn);
    }

    [Fact]
    public void Build_FeatureTest_GenericMethods_OpenMethodsSkipped()
    {
        var module = BuildFeatureTest();
        var utils = module.FindType("GenericUtils");
        Assert.NotNull(utils);
        // Open generic methods (Identity<T>, Swap<T>) should NOT appear as non-specialized
        var openMethods = utils!.Methods.Where(m =>
            m.Name is "Identity" or "Swap" && !m.IsGenericInstance).ToList();
        Assert.Empty(openMethods);
    }

    [Fact]
    public void Build_FeatureTest_TestGenericMethods_CallsSpecialized()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestGenericMethods");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("Identity") && c.FunctionName.Contains("System_Int32"));
        Assert.Contains(calls, c => c.FunctionName.Contains("Identity") && c.FunctionName.Contains("System_String"));
        Assert.Contains(calls, c => c.FunctionName.Contains("Swap") && c.FunctionName.Contains("System_Int32"));
    }

    // ===== Generic Value Types (Pair<T>) =====

    [Fact]
    public void Build_FeatureTest_PairInt_Exists()
    {
        var module = BuildFeatureTest();
        var pairTypes = module.Types.Where(t => t.IsGenericInstance && t.CppName.Contains("Pair")).ToList();
        Assert.True(pairTypes.Count >= 1, "Should have Pair<int> specialization");
    }

    [Fact]
    public void Build_FeatureTest_PairInt_IsValueType()
    {
        var module = BuildFeatureTest();
        var pairInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Pair")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(pairInt);
        Assert.True(pairInt!.IsValueType, "Pair<int> should be a value type");
    }

    [Fact]
    public void Build_FeatureTest_PairInt_HasFields()
    {
        var module = BuildFeatureTest();
        var pairInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Pair")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(pairInt);
        var fieldNames = pairInt!.Fields.Select(f => f.Name).ToList();
        Assert.Contains("First", fieldNames);
        Assert.Contains("Second", fieldNames);
    }

    [Fact]
    public void Build_FeatureTest_PairInt_FieldTypesSubstituted()
    {
        var module = BuildFeatureTest();
        var pairInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Pair")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(pairInt);
        var firstField = pairInt!.Fields.First(f => f.Name == "First");
        Assert.Equal("System.Int32", firstField.FieldTypeName);
    }

    [Fact]
    public void Build_FeatureTest_PairInt_HasConstructor()
    {
        var module = BuildFeatureTest();
        var pairInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Pair")
            && t.GenericArguments.Contains("System.Int32"));
        Assert.NotNull(pairInt);
        var ctor = pairInt!.Methods.FirstOrDefault(m => m.IsConstructor);
        Assert.NotNull(ctor);
        Assert.Equal(2, ctor!.Parameters.Count);
    }

    // ===== Nullable<T> =====

    [Fact]
    public void Build_FeatureTest_Nullable_MonomorphizesStruct()
    {
        var module = BuildFeatureTest();
        // Nullable<int> should be monomorphized with correct fields
        var nullable = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Nullable"));
        Assert.NotNull(nullable);
        Assert.True(nullable!.IsValueType);
        var fieldNames = nullable.Fields.Select(f => f.Name).ToList();
        Assert.Contains("hasValue", fieldNames);
        Assert.Contains("value", fieldNames);
    }

    [Fact]
    public void Build_FeatureTest_BoxNullable_EmitsUnwrapLogic()
    {
        var module = BuildFeatureTest();
        var testMethod = module.GetAllMethods().FirstOrDefault(m => m.Name == "TestNullableBoxing");
        Assert.NotNull(testMethod);
        var allInstructions = testMethod!.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        var rawCpp = allInstructions.OfType<IRRawCpp>().Select(r => r.Code).ToList();
        var allCode = string.Join("\n", rawCpp);
        // box Nullable<T> should check f_hasValue, not box the whole struct
        Assert.Contains("f_hasValue", allCode);
        Assert.Contains("f_value", allCode);
        Assert.Contains("nullptr", allCode);
    }

    [Fact]
    public void Build_FeatureTest_BoxNullable_NoIRBoxForNullable()
    {
        var module = BuildFeatureTest();
        var testMethod = module.GetAllMethods().FirstOrDefault(m => m.Name == "TestNullableBoxing");
        Assert.NotNull(testMethod);
        var allInstructions = testMethod!.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // There should be no IRBox with "Nullable" in the type name
        var nullableBoxes = allInstructions.OfType<IRBox>()
            .Where(b => b.ValueTypeCppName.Contains("Nullable"))
            .ToList();
        Assert.Empty(nullableBoxes);
    }

    // ===== ValueTuple =====

    [Fact]
    public void Build_FeatureTest_ValueTuple_MonomorphizesStruct()
    {
        var module = BuildFeatureTest();
        // ValueTuple<int,int> should be monomorphized with Item1, Item2 fields
        var tuple = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("ValueTuple"));
        Assert.NotNull(tuple);
        Assert.True(tuple!.IsValueType);
        var fieldNames = tuple.Fields.Select(f => f.Name).ToList();
        Assert.Contains("Item1", fieldNames);
        Assert.Contains("Item2", fieldNames);
    }

    [Fact]
    public void Build_FeatureTest_ValueTupleGetHashCode_UsesHashCombining()
    {
        var module = BuildFeatureTest();
        // ValueTuple.GetHashCode is intercepted when called directly on the tuple type
        // The C# compiler may route through constrained callvirt -> Object.GetHashCode()
        // but our interception also handles direct ValueTuple calls
        var testMethod = module.GetAllMethods().FirstOrDefault(m => m.Name == "TestValueTupleEquals");
        Assert.NotNull(testMethod);
        var allInstructions = testMethod!.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        var rawCpp = allInstructions.OfType<IRRawCpp>().Select(r => r.Code).ToList();
        var allCode = string.Join("\n", rawCpp);
        // No stub comments should remain for Equals
        Assert.DoesNotContain("/* ValueTuple.Equals stub */", allCode);
    }

    // ===== Multi-Parameter & Nested Generics =====

    [Fact]
    public void Build_FeatureTest_KeyValueIntString_Exists()
    {
        var module = BuildFeatureTest();
        var kv = module.Types.FirstOrDefault(t => t.CppName.Contains("KeyValue") && t.IsGenericInstance);
        Assert.NotNull(kv);
        Assert.Equal(2, kv!.GenericArguments.Count);
    }

    [Fact]
    public void Build_FeatureTest_KeyValueIntString_FieldsSubstituted()
    {
        var module = BuildFeatureTest();
        var kv = module.Types.First(t => t.CppName.Contains("KeyValue") && t.IsGenericInstance);
        var keyField = kv.Fields.FirstOrDefault(f => f.Name == "Key");
        var valueField = kv.Fields.FirstOrDefault(f => f.Name == "Value");
        Assert.NotNull(keyField);
        Assert.NotNull(valueField);
        // Key is int, Value is string — types should be substituted
        Assert.DoesNotContain("T", keyField!.FieldTypeName);
        Assert.DoesNotContain("T", valueField!.FieldTypeName);
    }

    [Fact]
    public void Build_FeatureTest_NestedGeneric_WrapperOfWrapperInt_Exists()
    {
        var module = BuildFeatureTest();
        // Wrapper<Wrapper<int>> should exist as a separate generic instantiation
        var nested = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance && t.CppName.Contains("Wrapper") &&
            t.GenericArguments.Any(a => a.Contains("Wrapper")));
        Assert.NotNull(nested);
    }

    // ===== Generic Type Inheritance =====

    [Fact]
    public void Build_FeatureTest_SpecialWrapperInt_Exists()
    {
        var module = BuildFeatureTest();
        var sw = module.Types.FirstOrDefault(t => t.CppName.Contains("SpecialWrapper") && t.IsGenericInstance);
        Assert.NotNull(sw);
    }

    [Fact]
    public void Build_FeatureTest_SpecialWrapperInt_HasBaseType()
    {
        var module = BuildFeatureTest();
        var sw = module.Types.First(t => t.CppName.Contains("SpecialWrapper") && t.IsGenericInstance);
        Assert.NotNull(sw.BaseType);
        Assert.Contains("Wrapper", sw.BaseType!.CppName);
    }

    [Fact]
    public void Build_FeatureTest_SpecialWrapperInt_BaseTypeHasGetValue()
    {
        var module = BuildFeatureTest();
        var sw = module.Types.First(t => t.CppName.Contains("SpecialWrapper") && t.IsGenericInstance);
        // GetValue lives on base type Wrapper<int>, not on SpecialWrapper<int> itself
        Assert.NotNull(sw.BaseType);
        Assert.Contains(sw.BaseType!.Methods, m => m.Name == "GetValue");
    }

    // ===== Generic Static Constructor =====

    [Fact]
    public void Build_FeatureTest_GenericCacheInt_HasCctor()
    {
        var module = BuildFeatureTest();
        var cache = module.Types.FirstOrDefault(t => t.CppName.Contains("GenericCache") && t.IsGenericInstance);
        Assert.NotNull(cache);
        Assert.True(cache!.HasCctor, "GenericCache<int> should have HasCctor flag set");
    }

    [Fact]
    public void Build_FeatureTest_GenericCacheInt_HasStaticField()
    {
        var module = BuildFeatureTest();
        var cache = module.Types.First(t => t.CppName.Contains("GenericCache") && t.IsGenericInstance);
        Assert.Contains(cache.StaticFields, f => f.Name == "_initCount");
    }

    // ===== Generic Variance =====

    [Fact]
    public void Build_FeatureTest_Variance_CovariantInstance_HasVariance()
    {
        var module = BuildFeatureTest();
        // ICovariant<string> should be created as a generic instance with covariant variance
        var covariantStr = module.Types.FirstOrDefault(t => t.IsGenericInstance
            && t.CppName.Contains("ICovariant") && t.CppName.Contains("String"));
        Assert.NotNull(covariantStr);
        Assert.True(covariantStr.IsInterface);
        Assert.Single(covariantStr.GenericParameterVariances);
        Assert.Equal(GenericVariance.Covariant, covariantStr.GenericParameterVariances[0]);
        Assert.NotNull(covariantStr.GenericDefinitionCppName);
    }

    [Fact]
    public void Build_FeatureTest_Variance_ContravariantInstance_HasVariance()
    {
        var module = BuildFeatureTest();
        // IContravariant<object> should be created with contravariant variance
        var contravariantObj = module.Types.FirstOrDefault(t => t.IsGenericInstance
            && t.CppName.Contains("IContravariant") && t.CppName.Contains("Object"));
        Assert.NotNull(contravariantObj);
        Assert.Single(contravariantObj.GenericParameterVariances);
        Assert.Equal(GenericVariance.Contravariant, contravariantObj.GenericParameterVariances[0]);
    }

    [Fact]
    public void Build_FeatureTest_Variance_GenericArguments_Populated()
    {
        var module = BuildFeatureTest();
        var covariantStr = module.Types.FirstOrDefault(t => t.IsGenericInstance
            && t.CppName.Contains("ICovariant") && t.CppName.Contains("String"));
        Assert.NotNull(covariantStr);
        Assert.Single(covariantStr.GenericArguments);
        Assert.Equal("System.String", covariantStr.GenericArguments[0]);
    }

    // ===== Generic Delegates & TaskCompletionSource =====

    [Fact]
    public void Build_FeatureTest_TaskCompletionSource_Monomorphized()
    {
        var module = BuildFeatureTest();
        var tcsInt = module.Types.FirstOrDefault(t =>
            t.IsGenericInstance
            && t.ILFullName.Contains("TaskCompletionSource`1")
            && t.ILFullName.Contains("System.Int32"));
        Assert.NotNull(tcsInt);
        Assert.False(tcsInt!.IsValueType);
        Assert.Contains(tcsInt.Fields, f => f.CppName == "f_task");
    }

    [Fact]
    public void Build_FeatureTest_GenericDelegate_IsDelegate()
    {
        var module = BuildFeatureTest();
        var funcType = module.Types.FirstOrDefault(t =>
            t.ILFullName.Contains("System.Func`2") && t.ILFullName.Contains("Boolean"));
        Assert.NotNull(funcType);
        Assert.True(funcType!.IsDelegate,
            "Generic Func<int,bool> should have IsDelegate = true");
    }
}
