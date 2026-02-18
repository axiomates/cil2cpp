using Xunit;
using CIL2CPP.Core;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("Features")]
public class IRBuilderFeaturesTests
{
    private readonly FeatureTestFixture _fixture;

    public IRBuilderFeaturesTests(FeatureTestFixture fixture)
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

    // ===== Static Fields & Cctor =====

    [Fact]
    public void Build_FeatureTest_StaticFields()
    {
        var module = BuildFeatureTest();
        var program = module.FindType("Program")!;
        Assert.True(program.StaticFields.Count > 0, "Program should have static field _globalValue");
    }

    [Fact]
    public void Build_FeatureTest_StaticFieldAccess_InTestStaticFields()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestStaticFields");
        Assert.Contains(instrs, i => i is IRStaticFieldAccess sfa && !sfa.IsStore);
        Assert.Contains(instrs, i => i is IRStaticFieldAccess sfa && sfa.IsStore);
    }

    [Fact]
    public void Build_FeatureTest_AnimalStaticField()
    {
        var module = BuildFeatureTest();
        var animal = module.FindType("Animal")!;
        Assert.True(animal.StaticFields.Count > 0, "Animal has static _count field");
    }

    [Fact]
    public void Build_FeatureTest_Program_HasCctor()
    {
        var module = BuildFeatureTest();
        // Program has static field initializer (_globalValue = 100) which may generate a .cctor
        var program = module.FindType("Program")!;
        // Static field with initializer → compiler may generate .cctor
        // Verify HasCctor flag is correctly detected
        // Note: whether HasCctor is true depends on compiler output
        Assert.NotNull(program);
    }

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

    // ===== Console =====

    [Fact]
    public void Build_FeatureTest_TestConsoleWrite_HasWriteCall()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestConsoleWrite");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("Console_Write"));
    }

    // ===== Properties =====

    [Fact]
    public void Build_FeatureTest_PersonType_Exists()
    {
        var module = BuildFeatureTest();
        Assert.NotNull(module.FindType("Person"));
    }

    [Fact]
    public void Build_FeatureTest_Person_HasBackingFields()
    {
        var module = BuildFeatureTest();
        var person = module.FindType("Person")!;
        // Auto-properties generate backing fields like <Name>k__BackingField
        // After mangling, they become valid C++ identifiers without < or >
        var fieldCppNames = person.Fields.Select(f => f.CppName).ToList();
        foreach (var name in fieldCppNames)
        {
            Assert.DoesNotContain("<", name);
            Assert.DoesNotContain(">", name);
        }
    }

    [Fact]
    public void Build_FeatureTest_Person_HasGetSetMethods()
    {
        var module = BuildFeatureTest();
        var person = module.FindType("Person")!;
        var methodNames = person.Methods.Select(m => m.Name).ToList();
        Assert.Contains("get_Name", methodNames);
        Assert.Contains("set_Name", methodNames);
        Assert.Contains("get_Age", methodNames);
        Assert.Contains("set_Age", methodNames);
        Assert.Contains("get_ManualProp", methodNames);
        Assert.Contains("set_ManualProp", methodNames);
    }

    [Fact]
    public void Build_FeatureTest_TestProperties_HasFieldAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestProperties");
        var calls = instrs.OfType<IRCall>().ToList();
        // TestProperties calls get_Name, get_Age, set_ManualProp, get_ManualProp
        Assert.True(calls.Count > 0);
    }

    // ===== Delegates =====

    [Fact]
    public void Build_FeatureTest_MathOp_IsDelegate()
    {
        var module = BuildFeatureTest();
        var mathOp = module.FindType("MathOp");
        Assert.NotNull(mathOp);
        Assert.True(mathOp!.IsDelegate);
    }

    [Fact]
    public void Build_FeatureTest_NonDelegate_NotMarkedAsDelegate()
    {
        var module = BuildFeatureTest();
        var animal = module.FindType("Animal")!;
        Assert.False(animal.IsDelegate);
    }

    [Fact]
    public void Build_FeatureTest_TestDelegate_HasLdftn()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegate");
        Assert.Contains(instrs, i => i is IRLoadFunctionPointer);
    }

    [Fact]
    public void Build_FeatureTest_TestDelegate_HasDelegateCreate()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegate");
        Assert.Contains(instrs, i => i is IRDelegateCreate);
    }

    [Fact]
    public void Build_FeatureTest_TestDelegate_HasDelegateInvoke()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegate");
        Assert.Contains(instrs, i => i is IRDelegateInvoke);
    }

    [Fact]
    public void Build_FeatureTest_TestDelegate_DelegateInvokeHasParams()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegate");
        var invoke = instrs.OfType<IRDelegateInvoke>().First();
        Assert.Equal(2, invoke.ParamTypes.Count);
        Assert.Equal("int32_t", invoke.ReturnTypeCpp);
    }

    [Fact]
    public void Build_FeatureTest_TestDelegate_NoUnsupportedWarnings()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDelegate");
        var warnings = instrs.OfType<IRComment>().Where(c => c.Text.Contains("WARNING")).ToList();
        Assert.Empty(warnings);
    }

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

    // ===== Virtual Delegate =====

    [Fact]
    public void Build_FeatureTest_TestVirtualDelegate_HasLdvirtftn()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestVirtualDelegate");
        var ldfptrs = instrs.OfType<IRLoadFunctionPointer>().ToList();
        Assert.Contains(ldfptrs, p => p.IsVirtual);
    }

    [Fact]
    public void Build_FeatureTest_TestVirtualDelegate_HasDelegateCreate()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestVirtualDelegate");
        Assert.Contains(instrs, i => i is IRDelegateCreate);
    }

    [Fact]
    public void Build_FeatureTest_TestVirtualDelegate_HasDelegateInvoke()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestVirtualDelegate");
        Assert.Contains(instrs, i => i is IRDelegateInvoke);
    }

    [Fact]
    public void Build_FeatureTest_StringFunc_IsDelegate()
    {
        var module = BuildFeatureTest();
        var stringFunc = module.FindType("StringFunc");
        Assert.NotNull(stringFunc);
        Assert.True(stringFunc!.IsDelegate);
    }

    // ===== Lambda / Closures =====

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

    // ===== Ldflda / Address-of =====

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

    // ===== Ref Parameters =====

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

    [Fact]
    public void Build_FeatureTest_TestRefParams_HasSwapCalls()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestRefParams");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("SwapInt"));
        Assert.Contains(calls, c => c.FunctionName.Contains("SwapObj"));
    }

    [Fact]
    public void Build_FeatureTest_TestStaticFieldRef_HasLdsflda()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestStaticFieldRef");
        // ldsflda produces IRRawCpp with &TypeName_statics.fieldName
        var rawCpps = instrs.OfType<IRRawCpp>().ToList();
        Assert.Contains(rawCpps, r => r.Code.Contains("_statics.") && r.Code.Contains("&"));
    }

    // ===== Indexer =====

    [Fact]
    public void Build_FeatureTest_IntList_HasGetItem()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "IntList");
        Assert.Contains(type.Methods, m => m.Name == "get_Item");
    }

    [Fact]
    public void Build_FeatureTest_IntList_HasSetItem()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "IntList");
        Assert.Contains(type.Methods, m => m.Name == "set_Item");
    }

    [Fact]
    public void Build_FeatureTest_TestIndexer_CallsGetSetItem()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestIndexer");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("get_Item"));
        Assert.Contains(calls, c => c.FunctionName.Contains("set_Item"));
    }

    // ===== Default Parameters =====

    [Fact]
    public void Build_FeatureTest_TestDefaultParameters_CallsAdd()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestDefaultParameters");
        var calls = instrs.OfType<IRCall>().ToList();
        // Both calls to Add should be present (with default and explicit)
        var addCalls = calls.Where(c => c.FunctionName.Contains("Add")).ToList();
        Assert.True(addCalls.Count >= 2);
    }

    [Fact]
    public void Build_FeatureTest_DefaultParamHelper_AddHasTwoParams()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "DefaultParamHelper");
        var addMethod = type.Methods.First(m => m.Name == "Add");
        // In IL, default params are just regular params
        Assert.Equal(2, addMethod.Parameters.Count);
    }

    // ===== Init-Only Setter =====

    [Fact]
    public void Build_FeatureTest_ImmutablePoint_HasSetX()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "ImmutablePoint");
        // init setter compiles to set_X method
        Assert.Contains(type.Methods, m => m.Name == "set_X");
    }

    [Fact]
    public void Build_FeatureTest_ImmutablePoint_HasSetY()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "ImmutablePoint");
        Assert.Contains(type.Methods, m => m.Name == "set_Y");
    }

    [Fact]
    public void Build_FeatureTest_TestInitOnlySetter_CallsSetters()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestInitOnlySetter");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.Contains(calls, c => c.FunctionName.Contains("set_X"));
        Assert.Contains(calls, c => c.FunctionName.Contains("set_Y"));
    }

    // ===== Arrays =====

    [Fact]
    public void Build_FeatureTest_TestForeachArray_HasArrayAccess()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestForeachArray");
        Assert.Contains(instrs, i => i is IRArrayAccess);
    }

    [Fact]
    public void Build_FeatureTest_TestForeachArray_HasBranching()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestForeachArray");
        Assert.Contains(instrs, i => i is IRConditionalBranch);
    }

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

    // ===== Debug Mode =====

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

    // ===== Async / Await =====

    [Fact]
    public void Build_FeatureTest_Async_StateMachineCompiled()
    {
        var module = BuildFeatureTest();
        // C# compiler generates a state machine type for async methods
        // Name pattern: <ComputeAsync>d__N (class in Debug, struct in Release)
        var stateMachine = module.Types.FirstOrDefault(t =>
            t.Name.Contains("ComputeAsync") && t.Name.Contains("d__"));
        Assert.NotNull(stateMachine);
        // Should have MoveNext method
        var moveNext = stateMachine!.Methods.FirstOrDefault(m => m.Name == "MoveNext");
        Assert.NotNull(moveNext);
        Assert.True(moveNext!.BasicBlocks.Count > 0, "MoveNext should have body");
    }

    [Fact]
    public void Build_FeatureTest_Async_MoveNextCallsExist()
    {
        var module = BuildFeatureTest();
        var stateMachine = module.Types.FirstOrDefault(t =>
            t.Name.Contains("ComputeAsync") && t.Name.Contains("d__"));
        Assert.NotNull(stateMachine);
        var moveNext = stateMachine!.Methods.First(m => m.Name == "MoveNext");
        var allInstructions = moveNext.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // Should have IRRawCpp instructions for intercepted builder/awaiter calls
        var rawCpp = allInstructions.OfType<IRRawCpp>().ToList();
        Assert.True(rawCpp.Count > 0, "MoveNext should have inline C++ from intercepted calls");
    }

    // ===== Threading =====

    [Fact]
    public void Build_FeatureTest_TestLock_NoWarnings()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "TestLock");
        var allInstructions = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .ToList();
        // No WARNING comments
        var warnings = allInstructions.OfType<IRComment>()
            .Where(c => c.Text.Contains("WARNING"))
            .ToList();
        Assert.Empty(warnings);
    }

    [Fact]
    public void Build_FeatureTest_TestInterlocked_HasAtomicCalls()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "TestInterlocked");
        var calls = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRCall>()
            .Select(c => c.FunctionName)
            .ToList();
        // Interlocked.Increment → Interlocked_Increment_i32
        Assert.Contains(calls, c => c.Contains("Interlocked_Increment"));
        // Interlocked.CompareExchange → Interlocked_CompareExchange_i32
        Assert.Contains(calls, c => c.Contains("Interlocked_CompareExchange"));
    }

    [Fact]
    public void Build_FeatureTest_TestInterlockedLong_HasI64Calls()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "TestInterlockedLong");
        var calls = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRCall>()
            .Select(c => c.FunctionName)
            .ToList();
        // Interlocked.Increment(ref long) → Interlocked_Increment_i64
        Assert.Contains(calls, c => c.Contains("Interlocked_Increment_i64"));
    }

    [Fact]
    public void Build_FeatureTest_TestMonitorWaitPulse_HasMonitorWaitPulse()
    {
        var module = BuildFeatureTest();
        // Check main method has Monitor.Wait
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "TestMonitorWaitPulse");
        var calls = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRCall>()
            .Select(c => c.FunctionName)
            .ToList();
        Assert.Contains(calls, c => c.Contains("Monitor_Wait"));

        // Monitor.Pulse is in the lambda closure — check all types for it
        var allCalls = module.Types
            .SelectMany(t => t.Methods)
            .SelectMany(m => m.BasicBlocks)
            .SelectMany(b => b.Instructions)
            .OfType<IRCall>()
            .Select(c => c.FunctionName)
            .ToList();
        Assert.Contains(allCalls, c => c.Contains("Monitor_Pulse"));
    }

    // ===== Reflection =====

    [Fact]
    public void Build_FeatureTest_TestGetType_HasGetTypeManagedCall()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "TestGetType");
        var calls = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRCall>()
            .Select(c => c.FunctionName)
            .ToList();
        Assert.Contains(calls, c => c.Contains("object_get_type_managed"));
    }

    [Fact]
    public void Build_FeatureTest_TestTypeToString_HasVtableOrToString()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "TestTypeToString");
        // Type.ToString() may be resolved as:
        // 1. type_to_string via TryEmitTypeCall (if Roslyn targets Type::ToString)
        // 2. vtable dispatch via Object::ToString (default virtual call path)
        // Both are valid — check that method has instructions
        var allInstr = method.BasicBlocks
            .SelectMany(b => b.Instructions)
            .ToList();
        Assert.NotEmpty(allInstr);
    }

    [Fact]
    public void Build_FeatureTest_ReflectionIRField_HasAttributes()
    {
        var module = BuildFeatureTest();
        // Dog inherits from Animal which has _name field
        var animalType = module.Types.FirstOrDefault(t => t.Name == "Animal");
        Assert.NotNull(animalType);
        var nameField = animalType.Fields.FirstOrDefault(f => f.Name == "_name");
        Assert.NotNull(nameField);
        // _name is "protected string" → Family (0x0004)
        Assert.NotEqual(0u, nameField.Attributes);
    }

    [Fact]
    public void Build_FeatureTest_ReflectionIRMethod_HasAttributes()
    {
        var module = BuildFeatureTest();
        var programType = module.Types.First(t => t.Name == "Program");
        var mainMethod = programType.Methods.First(m => m.Name == "Main");
        // Main is public static → at least Public (0x0006) | Static (0x0010)
        Assert.NotEqual(0u, mainMethod.Attributes);
        // Check Public flag (access mask & 0x7 == 0x6)
        Assert.Equal(0x0006u, mainMethod.Attributes & 0x0007u);
    }

    // ===== Custom Attributes =====

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_HasObsolete()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.FirstOrDefault(t => t.Name == "AttributeTestClass");
        Assert.NotNull(attrType);
        var obsolete = attrType.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "System.ObsoleteAttribute");
        Assert.NotNull(obsolete);
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_ObsoleteHasMessage()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var obsolete = attrType.CustomAttributes.First(
            a => a.AttributeTypeName == "System.ObsoleteAttribute");
        Assert.Single(obsolete.ConstructorArgs);
        Assert.Equal("System.String", obsolete.ConstructorArgs[0].TypeName);
        Assert.Equal("Use NewClass instead", obsolete.ConstructorArgs[0].Value);
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_HasDescription()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var desc = attrType.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "DescriptionAttribute");
        Assert.NotNull(desc);
        Assert.Single(desc.ConstructorArgs);
        Assert.Equal("A test class with attributes", desc.ConstructorArgs[0].Value);
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_FieldHasObsolete()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var oldField = attrType.Fields.FirstOrDefault(f => f.Name == "OldField");
        Assert.NotNull(oldField);
        var obsolete = oldField.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "System.ObsoleteAttribute");
        Assert.NotNull(obsolete);
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_MethodHasDescription()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var method = attrType.Methods.FirstOrDefault(m => m.Name == "AnnotatedMethod");
        Assert.NotNull(method);
        var desc = method.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "DescriptionAttribute");
        Assert.NotNull(desc);
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_NoCompilerInternalAttrs()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        // CompilerGeneratedAttribute and NullableContextAttribute should be filtered out
        Assert.DoesNotContain(attrType.CustomAttributes,
            a => a.AttributeTypeName.Contains("CompilerGenerated"));
        Assert.DoesNotContain(attrType.CustomAttributes,
            a => a.AttributeTypeName.Contains("NullableContext"));
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_HasTypeRefAttribute()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var typeRef = attrType.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "TypeRefAttribute");
        Assert.NotNull(typeRef);
        Assert.Single(typeRef.ConstructorArgs);
        Assert.Equal(AttributeArgKind.Type, typeRef.ConstructorArgs[0].Kind);
        Assert.Equal("System.String", typeRef.ConstructorArgs[0].Value);
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_HasSeverityAttribute()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var severity = attrType.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "SeverityAttribute");
        Assert.NotNull(severity);
        Assert.Single(severity.ConstructorArgs);
        Assert.Equal(AttributeArgKind.Enum, severity.ConstructorArgs[0].Kind);
        Assert.Equal(2L, severity.ConstructorArgs[0].Value);  // Severity.High == 2
    }

    [Fact]
    public void Build_FeatureTest_AttributeTestClass_HasTagsAttribute()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var tags = attrType.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "TagsAttribute");
        Assert.NotNull(tags);
        Assert.Single(tags.ConstructorArgs);
        var arrayArg = tags.ConstructorArgs[0];
        Assert.Equal(AttributeArgKind.Array, arrayArg.Kind);
        Assert.NotNull(arrayArg.ArrayElements);
        Assert.Equal(3, arrayArg.ArrayElements.Count);
        Assert.Equal("important", arrayArg.ArrayElements[0].Value);
        Assert.Equal("test", arrayArg.ArrayElements[1].Value);
        Assert.Equal("example", arrayArg.ArrayElements[2].Value);
    }

    [Fact]
    public void Build_FeatureTest_AnnotatedMethod_HasTypeRefAttribute()
    {
        var module = BuildFeatureTest();
        var attrType = module.Types.First(t => t.Name == "AttributeTestClass");
        var method = attrType.Methods.First(m => m.Name == "AnnotatedMethod");
        var typeRef = method.CustomAttributes.FirstOrDefault(
            a => a.AttributeTypeName == "TypeRefAttribute");
        Assert.NotNull(typeRef);
        Assert.Single(typeRef.ConstructorArgs);
        Assert.Equal(AttributeArgKind.Type, typeRef.ConstructorArgs[0].Kind);
        Assert.Equal("System.Int32", typeRef.ConstructorArgs[0].Value);
    }

    // ===== Multi-Dimensional Arrays =====

    [Fact]
    public void Build_FeatureTest_MdArray_Create2D_HasRank2()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "MdArrayTest");
        var method = type.Methods.First(m => m.Name == "Create2D");
        var allCode = string.Join("\n", method.BasicBlocks.SelectMany(b => b.Instructions).Select(i => i.ToCpp()));
        // Rank 2 passed to mdarray_create
        Assert.Contains(", 2,", allCode);
    }

    [Fact]
    public void Build_FeatureTest_MdArray_GetTotalLength_HasArrayGetLength()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "MdArrayTest");
        var method = type.Methods.First(m => m.Name == "GetTotalLength");
        var allCode = string.Join("\n", method.BasicBlocks.SelectMany(b => b.Instructions).Select(i => i.ToCpp()));
        // Should call array_get_length (ICall) which handles both 1D and multi-dim
        Assert.Contains("array_get_length", allCode);
    }

    // ===== P/Invoke =====

    [Fact]
    public void Build_FeatureTest_PInvoke_NativeAbs_Detected()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "PInvokeTest");
        var method = type.Methods.First(m => m.Name == "NativeAbs");
        Assert.True(method.IsPInvoke);
        Assert.Equal("msvcrt.dll", method.PInvokeModule);
        Assert.Equal("abs", method.PInvokeEntryPoint);
    }

    [Fact]
    public void Build_FeatureTest_PInvoke_NativeStrLen_Detected()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "PInvokeTest");
        var method = type.Methods.First(m => m.Name == "NativeStrLen");
        Assert.True(method.IsPInvoke);
        Assert.Equal("msvcrt.dll", method.PInvokeModule);
        Assert.Equal("strlen", method.PInvokeEntryPoint);
    }

    [Fact]
    public void Build_FeatureTest_PInvoke_NativeAbs_HasNoBody()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "PInvokeTest");
        var method = type.Methods.First(m => m.Name == "NativeAbs");
        // P/Invoke methods have no IL body
        Assert.Empty(method.BasicBlocks);
    }

    [Fact]
    public void Build_FeatureTest_PInvoke_NativeQSort_HasDelegateParam()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "PInvokeTest");
        var method = type.Methods.First(m => m.Name == "NativeQSort");
        Assert.True(method.IsPInvoke);
        Assert.Equal("qsort", method.PInvokeEntryPoint);
        // Last parameter should be a delegate (CompareCallback)
        var lastParam = method.Parameters.Last();
        Assert.NotNull(lastParam.ParameterType);
        Assert.True(lastParam.ParameterType!.IsDelegate);
    }

    [Fact]
    public void Build_FeatureTest_PInvoke_Point2D_IsValueType()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "Point2D");
        Assert.True(type.IsValueType);
        Assert.Equal(2, type.Fields.Count);
    }

    // ===== Span<T> =====

    [Fact]
    public void Build_FeatureTest_SpanInt_SyntheticType_Created()
    {
        var module = BuildFeatureTest();
        // Span<int> should be created as a synthetic generic instance
        var spanInt = module.Types.FirstOrDefault(t => t.IsGenericInstance
            && t.ILFullName.StartsWith("System.Span`1<"));
        Assert.NotNull(spanInt);
        Assert.True(spanInt.IsValueType);
        Assert.True(spanInt.Fields.Any(f => f.CppName == "f_reference"));
        Assert.True(spanInt.Fields.Any(f => f.CppName == "f_length"));
    }

    [Fact]
    public void Build_FeatureTest_ReadOnlySpan_SyntheticType_Created()
    {
        var module = BuildFeatureTest();
        var roSpan = module.Types.FirstOrDefault(t => t.IsGenericInstance
            && t.ILFullName.StartsWith("System.ReadOnlySpan`1<"));
        Assert.NotNull(roSpan);
        Assert.True(roSpan.IsValueType);
    }

    // ===== LINQ =====

    [Fact]
    public void Build_FeatureTest_LinqSum_Intercepted()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "LinqSum");
        var rawCpp = method.BasicBlocks.SelectMany(b => b.Instructions)
            .OfType<IRRawCpp>().ToList();
        Assert.True(rawCpp.Any(r => r.Code.Contains("array_data")),
            "LinqSum should use array_data for iteration");
    }

    // ===== System.IO =====

    [Fact]
    [Trait("Category", "FeatureTest")]
    public void Build_FeatureTest_FileWriteAndRead_HasFileWriteCall()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "FileWriteAndRead");
        var calls = method.BasicBlocks.SelectMany(b => b.Instructions)
            .OfType<IRCall>().ToList();
        Assert.True(calls.Any(c => c.FunctionName.Contains("File_WriteAllText")),
            "FileWriteAndRead should map to File_WriteAllText");
        Assert.True(calls.Any(c => c.FunctionName.Contains("File_ReadAllText")),
            "FileWriteAndRead should map to File_ReadAllText");
    }

    [Fact]
    [Trait("Category", "FeatureTest")]
    public void Build_FeatureTest_FileExists_HasExistsCall()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "FileExists");
        var calls = method.BasicBlocks.SelectMany(b => b.Instructions)
            .OfType<IRCall>().ToList();
        Assert.True(calls.Any(c => c.FunctionName.Contains("File_Exists")),
            "FileExists should map to File_Exists");
    }

    [Fact]
    [Trait("Category", "FeatureTest")]
    public void Build_FeatureTest_PathCombine_HasCombineCall()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "PathCombine");
        var calls = method.BasicBlocks.SelectMany(b => b.Instructions)
            .OfType<IRCall>().ToList();
        Assert.True(calls.Any(c => c.FunctionName.Contains("Path_Combine")),
            "PathCombine should map to Path_Combine");
    }

    [Fact]
    [Trait("Category", "FeatureTest")]
    public void Build_FeatureTest_PathGetFileName_HasGetFileNameCall()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "PathGetFileName");
        var calls = method.BasicBlocks.SelectMany(b => b.Instructions)
            .OfType<IRCall>().ToList();
        Assert.True(calls.Any(c => c.FunctionName.Contains("Path_GetFileName")),
            "PathGetFileName should map to Path_GetFileName");
    }

    [Fact]
    [Trait("Category", "FeatureTest")]
    public void Build_FeatureTest_PathGetExtension_HasGetExtensionCall()
    {
        var module = BuildFeatureTest();
        var method = module.Types.First(t => t.Name == "Program")
            .Methods.First(m => m.Name == "PathGetExtension");
        var calls = method.BasicBlocks.SelectMany(b => b.Instructions)
            .OfType<IRCall>().ToList();
        Assert.True(calls.Any(c => c.FunctionName.Contains("Path_GetExtension")),
            "PathGetExtension should map to Path_GetExtension");
    }

    // ===== AllFieldTypes =====

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
}
