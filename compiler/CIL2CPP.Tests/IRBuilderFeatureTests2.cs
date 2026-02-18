using Xunit;
using Mono.Cecil.Cil;
using CIL2CPP.Core;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("FeatureTest2")]
public class IRBuilderFeatureTests2
{
    private readonly FeatureTestFixture _fixture;

    public IRBuilderFeatureTests2(FeatureTestFixture fixture)
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

    // ===== Phase 3: Generics =====

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

    // ===== Rethrow instruction =====

    [Fact]
    public void Build_FeatureTest_TestRethrow_HasNestedTryCatch()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestRethrow");
        // Should have two TryBegin (outer + inner)
        var tryBegins = instrs.OfType<IRTryBegin>().ToList();
        Assert.True(tryBegins.Count >= 2, "TestRethrow should have nested try blocks");
    }

    // ===== Float/Double NaN and Infinity =====

    [Fact]
    public void Build_FeatureTest_TestSpecialFloats_HasAssigns()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestSpecialFloats");
        // The NaN/Infinity float/double values should produce assignments
        Assert.True(instrs.Count > 0, "TestSpecialFloats should have instructions");
        // Check that no WARNING comments were generated (all opcodes handled)
        var warnings = instrs.OfType<IRComment>().Where(c => c.Text.Contains("WARNING")).ToList();
        Assert.Empty(warnings);
    }

    // ===== Delegate.Combine and Delegate.Remove =====

    [Fact]
    public void Build_FeatureTest_TestDelegateCombine_HasCombineCall()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegateCombine");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("delegate_combine"));
    }

    [Fact]
    public void Build_FeatureTest_TestDelegateCombine_HasRemoveCall()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegateCombine");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("delegate_remove"));
    }

    // ===== Math.Abs overloads — [InternalCall] mapped to <cmath> via icall =====

    [Fact]
    public void Build_FeatureTest_TestMathAbsOverloads_HasAbsCalls()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestMathAbsOverloads");
        var calls = instrs.OfType<IRCall>().ToList();
        var funcNames = calls.Select(c => c.FunctionName).ToList();
        // Math.Abs overloads are [InternalCall] — mapped to cil2cpp::icall::Math_Abs_* via ICallRegistry
        Assert.Contains(funcNames, n => n.Contains("Math") || n.Contains("Abs") || n.Contains("abs"));
    }

    // ===== AllFieldTypes: field size coverage for Int16/Char/Int64/Double =====

    [Fact]
    public void Build_FeatureTest_AllFieldTypes_Exists()
    {
        var module = BuildFeatureTest();
        var type = module.FindType("AllFieldTypes");
        Assert.NotNull(type);
    }

    [Fact]
    public void Build_FeatureTest_AllFieldTypes_HasExpectedFields()
    {
        var module = BuildFeatureTest();
        var type = module.FindType("AllFieldTypes")!;
        var fieldNames = type.Fields.Select(f => f.Name).ToList();
        Assert.Contains("ShortField", fieldNames);
        Assert.Contains("CharField", fieldNames);
        Assert.Contains("LongField", fieldNames);
        Assert.Contains("DoubleField", fieldNames);
        Assert.Contains("ByteField", fieldNames);
        Assert.Contains("FloatField", fieldNames);
    }

    [Fact]
    public void Build_FeatureTest_AllFieldTypes_InstanceSize_IncludesAllFields()
    {
        var module = BuildFeatureTest();
        var type = module.FindType("AllFieldTypes")!;
        // Object header (16) + short(2) + char(2) + long(8) + double(8) + byte(1) + float(4) + padding
        Assert.True(type.InstanceSize >= 32, $"AllFieldTypes instance size should be >= 32, got {type.InstanceSize}");
    }

    [Fact]
    public void Build_FeatureTest_AllFieldTypes_FieldTypes()
    {
        var module = BuildFeatureTest();
        var type = module.FindType("AllFieldTypes")!;
        var shortField = type.Fields.First(f => f.Name == "ShortField");
        var charField = type.Fields.First(f => f.Name == "CharField");
        var longField = type.Fields.First(f => f.Name == "LongField");
        var doubleField = type.Fields.First(f => f.Name == "DoubleField");
        Assert.Equal("System.Int16", shortField.FieldTypeName);
        Assert.Equal("System.Char", charField.FieldTypeName);
        Assert.Equal("System.Int64", longField.FieldTypeName);
        Assert.Equal("System.Double", doubleField.FieldTypeName);
    }

    // ===== Virtual dispatch on System.Object =====

    [Fact]
    public void Build_FeatureTest_TestObjectMethods_VirtualDispatch()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestVirtualObjectDispatch");
        var calls = instrs.OfType<IRCall>().Where(c => c.IsVirtual).ToList();
        // ToString/GetHashCode/Equals on System.Object should be virtual calls
        Assert.True(calls.Count >= 2, "Virtual object dispatch should produce virtual IRCalls");
        Assert.True(calls.Any(c => c.VTableSlot >= 0), "Virtual calls should have VTableSlot");
    }

    // ===== Debug mode with FeatureTest (more coverage for debug paths) =====

    [Fact]
    public void Build_FeatureTest_Debug_InstructionsHaveSourceLocations()
    {
        var module = BuildFeatureTest(BuildConfiguration.Debug);
        var instrs = GetMethodInstructions(module, "Program", "TestArithmetic");
        var withSource = instrs.Where(i => i.DebugInfo != null && i.DebugInfo.Line > 0).ToList();
        Assert.True(withSource.Count > 0, "Debug FeatureTest should have source locations");
    }

    [Fact]
    public void Build_FeatureTest_Debug_HasFilePath()
    {
        var module = BuildFeatureTest(BuildConfiguration.Debug);
        var instrs = GetMethodInstructions(module, "Program", "TestArithmetic");
        var withFile = instrs.Where(i => i.DebugInfo?.FilePath != null).ToList();
        Assert.True(withFile.Count > 0, "Debug FeatureTest should have file paths");
        Assert.True(withFile[0].DebugInfo!.FilePath!.Contains("Program.cs"));
    }

    [Fact]
    public void Build_FeatureTest_Debug_ExceptionHandling_HasDebugInfo()
    {
        var module = BuildFeatureTest(BuildConfiguration.Debug);
        var instrs = GetMethodInstructions(module, "Program", "TestExceptionHandling");
        var tryInstrs = instrs.OfType<IRTryBegin>().ToList();
        Assert.True(tryInstrs.Count > 0);
        // In debug mode, exception handler instructions should have debug info
        var withDebug = instrs.Where(i => i.DebugInfo != null).ToList();
        Assert.True(withDebug.Count > instrs.Count / 2,
            "Most instructions should have debug info in debug mode");
    }

    // ===== Static constructor guard =====

    [Fact]
    public void Build_FeatureTest_StaticFieldAccess_HasCctorGuard()
    {
        var module = BuildFeatureTest();
        // Program has a static field _globalValue with initializer
        // Accessing static fields may trigger cctor guard
        var program = module.FindType("Program")!;
        if (program.HasCctor)
        {
            var instrs = GetMethodInstructions(module, "Program", "TestStaticFields");
            Assert.Contains(instrs, i => i is IRStaticCtorGuard);
        }
    }

    // ===== FeatureTest: Array creation with RawCpp =====

    [Fact]
    public void Build_FeatureTest_TestForeachArray_HasArrayCreation()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestForeachArray");
        // newarr produces IRRawCpp with array_create
        Assert.Contains(instrs, i => i is IRRawCpp raw && raw.Code.Contains("array_create"));
    }

    [Fact]
    public void Build_FeatureTest_TestForeachArray_HasArrayLength()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestForeachArray");
        Assert.Contains(instrs, i => i is IRRawCpp raw && raw.Code.Contains("array_length"));
    }

    // ===== Typed array element access (GetArrayElementType coverage) =====

    [Fact]
    public void Build_FeatureTest_TestTypedArrays_HasByteArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestTypedArrays");
        var accesses = instrs.OfType<IRArrayAccess>().ToList();
        Assert.Contains(accesses, a => a.ElementType == "uint8_t" && !a.IsStore);  // ldelem.u1
        Assert.Contains(accesses, a => a.ElementType == "int8_t" && a.IsStore);    // stelem.i1
    }

    [Fact]
    public void Build_FeatureTest_TestTypedArrays_HasShortArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestTypedArrays");
        var accesses = instrs.OfType<IRArrayAccess>().ToList();
        Assert.Contains(accesses, a => a.ElementType == "int16_t" && !a.IsStore);  // ldelem.i2
        Assert.Contains(accesses, a => a.ElementType == "int16_t" && a.IsStore);   // stelem.i2
    }

    [Fact]
    public void Build_FeatureTest_TestTypedArrays_HasLongArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestTypedArrays");
        var accesses = instrs.OfType<IRArrayAccess>().ToList();
        Assert.Contains(accesses, a => a.ElementType == "int64_t" && !a.IsStore);  // ldelem.i8
        Assert.Contains(accesses, a => a.ElementType == "int64_t" && a.IsStore);   // stelem.i8
    }

    [Fact]
    public void Build_FeatureTest_TestTypedArrays_HasFloatArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestTypedArrays");
        var accesses = instrs.OfType<IRArrayAccess>().ToList();
        Assert.Contains(accesses, a => a.ElementType == "float" && !a.IsStore);    // ldelem.r4
        Assert.Contains(accesses, a => a.ElementType == "float" && a.IsStore);     // stelem.r4
    }

    [Fact]
    public void Build_FeatureTest_TestTypedArrays_HasDoubleArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestTypedArrays");
        var accesses = instrs.OfType<IRArrayAccess>().ToList();
        Assert.Contains(accesses, a => a.ElementType == "double" && !a.IsStore);   // ldelem.r8
        Assert.Contains(accesses, a => a.ElementType == "double" && a.IsStore);    // stelem.r8
    }

    [Fact]
    public void Build_FeatureTest_TestTypedArrays_HasObjectArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestTypedArrays");
        var accesses = instrs.OfType<IRArrayAccess>().ToList();
        Assert.Contains(accesses, a => a.ElementType == "cil2cpp::Object*" && !a.IsStore); // ldelem.ref
        Assert.Contains(accesses, a => a.ElementType == "cil2cpp::Object*" && a.IsStore);  // stelem.ref
    }

    // ===== GenericHelper: generic type resolution in method bodies =====

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

    // ===== Generic Methods (standalone, not generic types) =====

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

    // ===== Lambda/Closures =====

    [Fact]
    public void Build_FeatureTest_Lambda_DisplayClass_Exists()
    {
        var module = BuildFeatureTest();
        // C# compiler generates <>c class for stateless lambdas
        var lambdaType = module.Types.FirstOrDefault(t => t.CppName.Contains("___c") && !t.CppName.Contains("DisplayClass"));
        Assert.NotNull(lambdaType);
    }

    [Fact]
    public void Build_FeatureTest_Lambda_DisplayClass_HasMethods()
    {
        var module = BuildFeatureTest();
        var lambdaType = module.Types.FirstOrDefault(t => t.CppName.Contains("___c") && !t.CppName.Contains("DisplayClass"));
        Assert.NotNull(lambdaType);
        // Should have lambda body methods (e.g., <TestLambda>b__43_0)
        Assert.True(lambdaType!.Methods.Count >= 2, "Lambda display class should have lambda body methods");
    }

    [Fact]
    public void Build_FeatureTest_Closure_DisplayClass_Exists()
    {
        var module = BuildFeatureTest();
        // C# compiler generates <>c__DisplayClass for closures
        var closureType = module.Types.FirstOrDefault(t => t.CppName.Contains("DisplayClass"));
        Assert.NotNull(closureType);
    }

    [Fact]
    public void Build_FeatureTest_Closure_DisplayClass_HasCapturedFields()
    {
        var module = BuildFeatureTest();
        var closureType = module.Types.FirstOrDefault(t => t.CppName.Contains("DisplayClass"));
        Assert.NotNull(closureType);
        // Should have captured variable fields
        Assert.True(closureType!.Fields.Count >= 1, "Closure display class should have captured variable fields");
    }

    [Fact]
    public void Build_FeatureTest_TestLambda_HasDelegateCreate()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestLambda");
        Assert.Contains(instrs, i => i is IRDelegateCreate);
    }

    [Fact]
    public void Build_FeatureTest_TestLambda_HasDelegateInvoke()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestLambda");
        Assert.Contains(instrs, i => i is IRDelegateInvoke);
    }

    [Fact]
    public void Build_FeatureTest_TestClosure_CreatesDelegateFromDisplayClass()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestClosure");
        // Should create display class instance (IRNewObj)
        Assert.Contains(instrs, i => i is IRNewObj);
        // Should create delegate from display class method (IRDelegateCreate)
        Assert.Contains(instrs, i => i is IRDelegateCreate);
        // Should invoke delegate (IRDelegateInvoke)
        Assert.Contains(instrs, i => i is IRDelegateInvoke);
    }

    [Fact]
    public void Build_FeatureTest_TestClosure_HasFieldAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestClosure");
        // Should store captured variable to display class field
        var fieldAccesses = instrs.OfType<IRFieldAccess>().Where(f => f.IsStore).ToList();
        Assert.True(fieldAccesses.Count >= 1, "Should store captured variables to display class");
    }

    // ===== Events =====

    [Fact]
    public void Build_FeatureTest_EventSource_HasDelegateField()
    {
        var module = BuildFeatureTest();
        var eventType = module.FindType("EventSource");
        Assert.NotNull(eventType);
        // Event backing field (delegate)
        Assert.True(eventType!.Fields.Count >= 1, "EventSource should have delegate backing field");
    }

    [Fact]
    public void Build_FeatureTest_EventSource_HasAddRemoveMethods()
    {
        var module = BuildFeatureTest();
        var eventType = module.FindType("EventSource");
        Assert.NotNull(eventType);
        var methodNames = eventType!.Methods.Select(m => m.Name).ToList();
        // C# generates add_ and remove_ accessor methods for events
        Assert.Contains("add_OnNotify", methodNames);
        Assert.Contains("remove_OnNotify", methodNames);
    }

    [Fact]
    public void Build_FeatureTest_EventSource_HasSubscribeFireMethods()
    {
        var module = BuildFeatureTest();
        var eventType = module.FindType("EventSource");
        Assert.NotNull(eventType);
        var methodNames = eventType!.Methods.Select(m => m.Name).ToList();
        Assert.Contains("Subscribe", methodNames);
        Assert.Contains("Unsubscribe", methodNames);
        Assert.Contains("Fire", methodNames);
    }

    [Fact]
    public void Build_FeatureTest_EventSource_FireMethod_HasDelegateInvoke()
    {
        var module = BuildFeatureTest();
        var fireMethod = module.FindType("EventSource")!.Methods.First(m => m.Name == "Fire");
        var instrs = fireMethod.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        Assert.Contains(instrs, i => i is IRDelegateInvoke);
    }

    [Fact]
    public void Build_FeatureTest_TestEvents_HasMethodCalls()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestEvents");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.True(calls.Count >= 3, "TestEvents should have Subscribe/Unsubscribe/Fire calls");
    }

    // ===== Ldflda / Ldobj / Stobj instructions =====

    [Fact]
    public void Build_FeatureTest_EventSource_AddMethod_HasLdflda()
    {
        var module = BuildFeatureTest();
        var addMethod = module.FindType("EventSource")!.Methods.First(m => m.Name == "add_OnNotify");
        var instrs = addMethod.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // The auto-generated add_ method uses ldflda for Interlocked.CompareExchange
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("&") && r.Code.Contains("->"));
    }

    // ===== Phase 2: User value type registration =====

    [Fact]
    public void Build_FeatureTest_UserValueType_Registered()
    {
        var module = BuildFeatureTest();
        // Point and Vector2 should be value types in the IR module
        var point = module.FindType("Point");
        var vector2 = module.FindType("Vector2");
        Assert.NotNull(point);
        Assert.NotNull(vector2);
        Assert.True(point!.IsValueType, "Point should be a value type");
        Assert.True(vector2!.IsValueType, "Vector2 should be a value type");
    }

    // ===== Phase 2: VTable seeded with Object methods =====

    [Fact]
    public void Build_FeatureTest_VTable_SeededWithObjectMethods()
    {
        var module = BuildFeatureTest();
        var animal = module.FindType("Animal")!;
        var vtableNames = animal.VTable.Select(e => e.MethodName).ToList();
        Assert.Contains("ToString", vtableNames);
        Assert.Contains("Equals", vtableNames);
        Assert.Contains("GetHashCode", vtableNames);
    }

    // ===== Ref parameters: Ldind_I4 / Stind_I4 =====

    [Fact]
    public void Build_FeatureTest_SwapInt_HasLdindI4()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "SwapInt");
        // ldind.i4 produces IRRawCpp with *(int32_t*) cast
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("*(int32_t*)"));
    }

    [Fact]
    public void Build_FeatureTest_SwapInt_HasStindI4()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "SwapInt");
        // stind.i4 produces IRRawCpp with *(int32_t*) assignment
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("*(int32_t*)") && r.Code.Contains("="));
    }

    // ===== Ref parameters: Ldind_Ref / Stind_Ref =====

    [Fact]
    public void Build_FeatureTest_SwapObj_HasLdindRef()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "SwapObj");
        // ldind.ref produces IRRawCpp with *(cil2cpp::Object**)
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("*(cil2cpp::Object**)"));
    }

    [Fact]
    public void Build_FeatureTest_SwapObj_HasStindRef()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "SwapObj");
        // stind.ref produces IRRawCpp with *(cil2cpp::Object**) assignment
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("*(cil2cpp::Object**)") && r.Code.Contains("="));
    }

    // ===== GetIndirectType covers all ldind/stind variants =====

    [Theory]
    [InlineData(Code.Ldind_I1, "int8_t")]
    [InlineData(Code.Ldind_I2, "int16_t")]
    [InlineData(Code.Ldind_I4, "int32_t")]
    [InlineData(Code.Ldind_I8, "int64_t")]
    [InlineData(Code.Ldind_U1, "uint8_t")]
    [InlineData(Code.Ldind_U2, "uint16_t")]
    [InlineData(Code.Ldind_U4, "uint32_t")]
    [InlineData(Code.Ldind_R4, "float")]
    [InlineData(Code.Ldind_R8, "double")]
    [InlineData(Code.Ldind_I, "intptr_t")]
    [InlineData(Code.Stind_I1, "int8_t")]
    [InlineData(Code.Stind_I2, "int16_t")]
    [InlineData(Code.Stind_I4, "int32_t")]
    [InlineData(Code.Stind_I8, "int64_t")]
    [InlineData(Code.Stind_R4, "float")]
    [InlineData(Code.Stind_R8, "double")]
    public void GetIndirectType_ReturnsCorrectCppType(Code code, string expectedCppType)
    {
        var result = IRBuilder.GetIndirectType(code);
        Assert.Equal(expectedCppType, result);
    }
}
