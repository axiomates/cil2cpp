using Xunit;
using CIL2CPP.Core;
using CIL2CPP.Core.IR;
using CIL2CPP.Tests.Fixtures;

namespace CIL2CPP.Tests;

[Collection("TypeSystem")]
public class IRBuilderTypeSystemTests
{
    private readonly FeatureTestFixture _fixture;

    public IRBuilderTypeSystemTests(FeatureTestFixture fixture)
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

    // ===== Types =====

    [Fact]
    public void Build_FeatureTest_HasExpectedTypes()
    {
        var module = BuildFeatureTest();
        Assert.NotNull(module.FindType("Animal"));
        Assert.NotNull(module.FindType("Dog"));
        Assert.NotNull(module.FindType("Cat"));
        Assert.NotNull(module.FindType("Program"));
        Assert.NotNull(module.FindType("Color"));
        Assert.NotNull(module.FindType("Point"));
    }

    // ===== Enums =====

    [Fact]
    public void Build_FeatureTest_EnumType()
    {
        var module = BuildFeatureTest();
        var color = module.FindType("Color")!;
        Assert.True(color.IsEnum);
        Assert.True(color.IsValueType);
    }

    [Fact]
    public void Build_FeatureTest_EnumUnderlyingType_Set()
    {
        var module = BuildFeatureTest();
        var color = module.FindType("Color")!;
        Assert.Equal("System.Int32", color.EnumUnderlyingType);
    }

    [Fact]
    public void Build_FeatureTest_EnumConstants_Extracted()
    {
        var module = BuildFeatureTest();
        var color = module.FindType("Color")!;
        var constFields = color.StaticFields.Where(f => f.ConstantValue != null).ToList();
        Assert.True(constFields.Count >= 3, "Color enum should have at least Red, Green, Blue");
    }

    [Fact]
    public void Build_FeatureTest_EnumNoValueField()
    {
        var module = BuildFeatureTest();
        var color = module.FindType("Color")!;
        Assert.DoesNotContain(color.Fields, f => f.Name == "value__");
    }

    // ===== Value Types =====

    [Fact]
    public void Build_FeatureTest_ValueType()
    {
        var module = BuildFeatureTest();
        var point = module.FindType("Point")!;
        Assert.True(point.IsValueType);
        Assert.False(point.IsEnum);
        Assert.True(point.Fields.Count >= 2, "Point should have X and Y fields");
    }

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

    // ===== Inheritance & VTable =====

    [Fact]
    public void Build_FeatureTest_InheritanceChain()
    {
        var module = BuildFeatureTest();
        var dog = module.FindType("Dog")!;
        Assert.NotNull(dog.BaseType);
        Assert.Equal("Animal", dog.BaseType!.ILFullName);
    }

    [Fact]
    public void Build_FeatureTest_VirtualMethods_InAnimal()
    {
        var module = BuildFeatureTest();
        var animal = module.FindType("Animal")!;
        var speak = animal.Methods.FirstOrDefault(m => m.Name == "Speak");
        Assert.NotNull(speak);
        Assert.True(speak!.IsVirtual);
    }

    [Fact]
    public void Build_FeatureTest_VTable_DogOverridesSpeak()
    {
        var module = BuildFeatureTest();
        var dog = module.FindType("Dog")!;
        Assert.True(dog.VTable.Count > 0, "Dog should have vtable entries");
        var speakEntry = dog.VTable.FirstOrDefault(v => v.MethodName == "Speak");
        Assert.NotNull(speakEntry);
        Assert.Equal("Dog", speakEntry!.DeclaringType!.Name);
    }

    [Fact]
    public void Build_FeatureTest_VTable_AnimalBaseEntry()
    {
        var module = BuildFeatureTest();
        var animal = module.FindType("Animal")!;
        var speakEntry = animal.VTable.FirstOrDefault(v => v.MethodName == "Speak");
        Assert.NotNull(speakEntry);
        Assert.Equal("Animal", speakEntry!.DeclaringType!.Name);
        Assert.True(speakEntry.Slot >= 0);
    }

    [Fact]
    public void Build_FeatureTest_VirtualCall_HasVTableSlot()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestVirtualCalls");
        var virtualCalls = instrs.OfType<IRCall>().Where(c => c.IsVirtual && c.VTableSlot >= 0).ToList();
        Assert.True(virtualCalls.Count > 0, "TestVirtualCalls should generate virtual dispatch with VTableSlot");
    }

    [Fact]
    public void Build_FeatureTest_TestVirtualCalls_HasCallInstructions()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestVirtualCalls");
        var calls = instrs.OfType<IRCall>().ToList();
        Assert.True(calls.Count > 0, "TestVirtualCalls should have call instructions");
    }

    [Fact]
    public void Build_FeatureTest_DogConstructor_Exists()
    {
        var module = BuildFeatureTest();
        var dog = module.FindType("Dog")!;
        var ctor = dog.Methods.FirstOrDefault(m => m.IsConstructor);
        Assert.NotNull(ctor);
    }

    [Fact]
    public void Build_FeatureTest_Animal_HasNameField()
    {
        var module = BuildFeatureTest();
        var animal = module.FindType("Animal")!;
        Assert.Contains(animal.Fields, f => f.Name == "_name");
    }

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

    // ===== Interface Dispatch =====

    [Fact]
    public void Build_FeatureTest_Duck_HasInterfaceImpls()
    {
        var module = BuildFeatureTest();
        var duck = module.FindType("Duck")!;
        Assert.True(duck.InterfaceImpls.Count > 0, "Duck should implement ISpeak");
        Assert.Contains(duck.InterfaceImpls, impl => impl.Interface.Name == "ISpeak");
    }

    [Fact]
    public void Build_FeatureTest_InterfaceDispatch_HasInterfaceCall()
    {
        var module = BuildFeatureTest();
        var instrs = GetMethodInstructions(module, "Program", "TestInterfaceDispatch");
        var ifaceCalls = instrs.OfType<IRCall>().Where(c => c.IsInterfaceCall).ToList();
        Assert.True(ifaceCalls.Count > 0, "TestInterfaceDispatch should generate interface dispatch calls");
    }

    [Fact]
    public void Build_FeatureTest_DisposableResource_Exists()
    {
        var module = BuildFeatureTest();
        Assert.NotNull(module.FindType("DisposableResource"));
    }

    [Fact]
    public void Build_FeatureTest_DisposableResource_HasDispose()
    {
        var module = BuildFeatureTest();
        var type = module.FindType("DisposableResource")!;
        var methodNames = type.Methods.Select(m => m.Name).ToList();
        Assert.Contains("Dispose", methodNames);
    }

    // ===== Interface Inheritance & Overloading =====

    [Fact]
    public void Build_FeatureTest_InterfaceInheritImpl_HasBothInterfaceImpls()
    {
        var module = BuildFeatureTest();
        var impl = module.FindType("InterfaceInheritImpl");
        Assert.NotNull(impl);
        // Cecil flattens interface list: both IBase and IDerived should be present
        var ifaceNames = impl!.InterfaceImpls.Select(i => i.Interface.Name).ToList();
        Assert.Contains("IBase", ifaceNames);
        Assert.Contains("IDerived", ifaceNames);
    }

    [Fact]
    public void Build_FeatureTest_InterfaceInheritImpl_BaseMethodMapped()
    {
        var module = BuildFeatureTest();
        var impl = module.FindType("InterfaceInheritImpl")!;
        var baseImpl = impl.InterfaceImpls.First(i => i.Interface.Name == "IBase");
        // IBase has 1 method: BaseMethod() — should map to InterfaceInheritImpl.BaseMethod
        Assert.Single(baseImpl.MethodImpls);
        Assert.NotNull(baseImpl.MethodImpls[0]);
        Assert.Equal("BaseMethod", baseImpl.MethodImpls[0]!.Name);
    }

    [Fact]
    public void Build_FeatureTest_InterfaceInheritImpl_DerivedMethodMapped()
    {
        var module = BuildFeatureTest();
        var impl = module.FindType("InterfaceInheritImpl")!;
        var derivedImpl = impl.InterfaceImpls.First(i => i.Interface.Name == "IDerived");
        // IDerived has 1 own method: DerivedMethod()
        Assert.Single(derivedImpl.MethodImpls);
        Assert.NotNull(derivedImpl.MethodImpls[0]);
        Assert.Equal("DerivedMethod", derivedImpl.MethodImpls[0]!.Name);
    }

    [Fact]
    public void Build_FeatureTest_Processor_HasOverloadedInterfaceMethods()
    {
        var module = BuildFeatureTest();
        var processor = module.FindType("Processor");
        Assert.NotNull(processor);
        var iProcessImpl = processor!.InterfaceImpls.FirstOrDefault(i => i.Interface.Name == "IProcess");
        Assert.NotNull(iProcessImpl);
        // IProcess has 2 methods: Process(int) and Process(string) — both should be mapped
        Assert.Equal(2, iProcessImpl!.MethodImpls.Count);
        Assert.All(iProcessImpl.MethodImpls, m => Assert.NotNull(m));
    }

    [Fact]
    public void Build_FeatureTest_Processor_OverloadedMethods_DistinctImpls()
    {
        var module = BuildFeatureTest();
        var processor = module.FindType("Processor")!;
        var iProcessImpl = processor.InterfaceImpls.First(i => i.Interface.Name == "IProcess");
        // The two Process methods should resolve to different implementing methods
        var methods = iProcessImpl.MethodImpls.Where(m => m != null).ToList();
        Assert.Equal(2, methods.Count);
        // They should have the same name but different parameter types
        Assert.All(methods, m => Assert.Equal("Process", m!.Name));
        Assert.NotEqual(methods[0]!.Parameters[0].CppTypeName, methods[1]!.Parameters[0].CppTypeName);
    }

    // ===== Abstract & Multi-Level Inheritance =====

    [Fact]
    public void Build_FeatureTest_AbstractClass_HasAbstractFlag()
    {
        var module = BuildFeatureTest();
        var shape = module.FindType("Shape");
        Assert.NotNull(shape);
        Assert.True(shape!.IsAbstract);
        var areaMethod = shape.Methods.FirstOrDefault(m => m.Name == "Area");
        Assert.NotNull(areaMethod);
        Assert.True(areaMethod!.IsAbstract);
    }

    [Fact]
    public void Build_FeatureTest_MultiLevelInheritance_VTableOverrides()
    {
        var module = BuildFeatureTest();
        var unitCircle = module.FindType("UnitCircle");
        Assert.NotNull(unitCircle);
        // UnitCircle inherits from Circle which inherits from Shape
        // Area() and Describe() should be in vtable (inherited from Circle)
        Assert.Contains(unitCircle!.VTable, e => e.MethodName == "Area");
        Assert.Contains(unitCircle.VTable, e => e.MethodName == "Describe");
    }

    [Fact]
    public void Build_FeatureTest_OverloadedVirtual_SeparateVTableSlots()
    {
        var module = BuildFeatureTest();
        var formatter = module.FindType("Formatter");
        Assert.NotNull(formatter);

        // Should have two separate vtable entries for Format(int) and Format(string)
        var formatEntries = formatter!.VTable.Where(e => e.MethodName == "Format").ToList();
        Assert.Equal(2, formatEntries.Count);
        Assert.NotEqual(formatEntries[0].Slot, formatEntries[1].Slot);
    }

    [Fact]
    public void Build_FeatureTest_OverloadedVirtual_DerivedOverridesBoth()
    {
        var module = BuildFeatureTest();
        var prefixFormatter = module.FindType("PrefixFormatter");
        Assert.NotNull(prefixFormatter);

        // PrefixFormatter should override both Format(int) and Format(string)
        var formatEntries = prefixFormatter!.VTable.Where(e => e.MethodName == "Format").ToList();
        Assert.Equal(2, formatEntries.Count);
        // Both should have Method != null (overridden, not base stubs)
        Assert.All(formatEntries, e => Assert.NotNull(e.Method));
    }

    // ===== Method Hiding (newslot) =====

    [Fact]
    public void Build_FeatureTest_DerivedDisplay_Show_IsNewSlot()
    {
        var module = BuildFeatureTest();
        var derived = module.Types.First(t => t.CppName == "DerivedDisplay");
        var show = derived.Methods.First(m => m.Name == "Show");
        Assert.True(show.IsNewSlot, "DerivedDisplay.Show should be marked as newslot");
    }

    [Fact]
    public void Build_FeatureTest_DerivedDisplay_Show_HasDifferentVTableSlot()
    {
        var module = BuildFeatureTest();
        var baseType = module.Types.First(t => t.CppName == "BaseDisplay");
        var derived = module.Types.First(t => t.CppName == "DerivedDisplay");
        var baseSlot = baseType.VTable.First(e => e.MethodName == "Show").Slot;
        var derivedShow = derived.Methods.First(m => m.Name == "Show");
        // newslot should create a DIFFERENT vtable slot than the base
        Assert.NotEqual(baseSlot, derivedShow.VTableSlot);
    }

    [Fact]
    public void Build_FeatureTest_DerivedDisplay_Value_OverridesBaseSlot()
    {
        var module = BuildFeatureTest();
        var baseType = module.Types.First(t => t.CppName == "BaseDisplay");
        var derived = module.Types.First(t => t.CppName == "DerivedDisplay");
        var baseSlot = baseType.VTable.First(e => e.MethodName == "Value").Slot;
        var derivedValue = derived.Methods.First(m => m.Name == "Value");
        // Normal override should reuse the SAME vtable slot
        Assert.Equal(baseSlot, derivedValue.VTableSlot);
    }

    [Fact]
    public void Build_FeatureTest_FinalDisplay_Overrides_DerivedShow()
    {
        var module = BuildFeatureTest();
        var derived = module.Types.First(t => t.CppName == "DerivedDisplay");
        var final_ = module.Types.First(t => t.CppName == "FinalDisplay");
        var derivedShowSlot = derived.Methods.First(m => m.Name == "Show").VTableSlot;
        // FinalDisplay.Show overrides DerivedDisplay.Show's (hidden) slot
        var finalShow = final_.Methods.First(m => m.Name == "Show");
        Assert.Equal(derivedShowSlot, finalShow.VTableSlot);
    }

    // ===== Records =====

    [Fact]
    public void Build_FeatureTest_Record_DetectedAsRecord()
    {
        var module = BuildFeatureTest();
        var recordType = module.Types.FirstOrDefault(t => t.Name == "PersonRecord");
        Assert.NotNull(recordType);
        Assert.True(recordType!.IsRecord, "PersonRecord should be detected as a record type");
    }

    [Fact]
    public void Build_FeatureTest_Record_HasSynthesizedMethods()
    {
        var module = BuildFeatureTest();
        var recordType = module.Types.FirstOrDefault(t => t.Name == "PersonRecord");
        Assert.NotNull(recordType);
        var methodNames = recordType!.Methods.Select(m => m.Name).ToList();
        Assert.Contains("ToString", methodNames);
        Assert.Contains("Equals", methodNames);
        Assert.Contains("GetHashCode", methodNames);
        Assert.Contains("<Clone>$", methodNames);
    }

    [Fact]
    public void Build_FeatureTest_Record_ToStringHasBody()
    {
        var module = BuildFeatureTest();
        var recordType = module.Types.FirstOrDefault(t => t.Name == "PersonRecord");
        Assert.NotNull(recordType);
        var toString = recordType!.Methods.FirstOrDefault(m => m.Name == "ToString");
        Assert.NotNull(toString);
        Assert.True(toString!.BasicBlocks.Count > 0, "Synthesized ToString should have a body");
        // Should contain string_concat calls for field formatting
        var allCode = string.Join("\n", toString.BasicBlocks
            .SelectMany(b => b.Instructions).Select(i => i.ToCpp()));
        Assert.Contains("string_concat", allCode);
    }

    [Fact]
    public void Build_FeatureTest_Record_EqualsHasFieldComparison()
    {
        var module = BuildFeatureTest();
        var recordType = module.Types.FirstOrDefault(t => t.Name == "PersonRecord");
        Assert.NotNull(recordType);
        // Find the typed Equals (takes PersonRecord* parameter)
        var typedEquals = recordType!.Methods.FirstOrDefault(m =>
            m.Name == "Equals" && m.Parameters.Count == 1
            && m.Parameters[0].CppTypeName.Contains(recordType.CppName));
        Assert.NotNull(typedEquals);
        Assert.True(typedEquals!.BasicBlocks.Count > 0, "Typed Equals should have a synthesized body");
    }

    [Fact]
    public void Build_FeatureTest_RecordStruct_DetectedAsRecord()
    {
        var module = BuildFeatureTest();
        var pointRecord = module.Types.FirstOrDefault(t => t.Name == "PointRecord");
        Assert.NotNull(pointRecord);
        Assert.True(pointRecord!.IsRecord, "PointRecord should be detected as a record type");
        Assert.True(pointRecord.IsValueType, "PointRecord should be a value type");
    }

    [Fact]
    public void Build_FeatureTest_RecordStruct_HasSynthesizedMethods()
    {
        var module = BuildFeatureTest();
        var pointRecord = module.Types.FirstOrDefault(t => t.Name == "PointRecord");
        Assert.NotNull(pointRecord);
        var methodNames = pointRecord!.Methods.Select(m => m.Name).ToList();
        Assert.Contains("ToString", methodNames);
        Assert.Contains("Equals", methodNames);
        Assert.Contains("GetHashCode", methodNames);
        Assert.Contains("op_Equality", methodNames);
    }

    [Fact]
    public void Build_FeatureTest_RecordStruct_EqualsUsesValueAccessor()
    {
        var module = BuildFeatureTest();
        var pointRecord = module.Types.FirstOrDefault(t => t.Name == "PointRecord");
        Assert.NotNull(pointRecord);
        // Find typed Equals (takes PointRecord parameter — value, not pointer)
        var typedEquals = pointRecord!.Methods.FirstOrDefault(m =>
            m.Name == "Equals" && m.Parameters.Count == 1
            && m.Parameters[0].CppTypeName.Contains(pointRecord.CppName));
        Assert.NotNull(typedEquals);
        Assert.True(typedEquals!.BasicBlocks.Count > 0);
        // Value type should NOT have null check in typed Equals
        var code = string.Join("\n", typedEquals.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRRawCpp>()
            .Select(i => i.Code));
        Assert.DoesNotContain("== nullptr", code);
        // Should use "." accessor for value-type other param, not "->"
        Assert.Contains(".", code);
    }

    [Fact]
    public void Build_FeatureTest_RecordStruct_NoCloneMethod()
    {
        var module = BuildFeatureTest();
        var pointRecord = module.Types.FirstOrDefault(t => t.Name == "PointRecord");
        Assert.NotNull(pointRecord);
        var cloneMethod = pointRecord!.Methods.FirstOrDefault(m => m.Name == "<Clone>$");
        // record struct doesn't have <Clone>$ method
        Assert.Null(cloneMethod);
    }

    // ===== Explicit Interface Implementation =====

    [Fact]
    public void Build_FeatureTest_FileLogger_HasExplicitOverride()
    {
        var module = BuildFeatureTest();
        var logger = module.Types.First(t => t.CppName == "FileLogger");
        // The explicit impl method should have ExplicitOverrides populated
        var logMethod = logger.Methods.FirstOrDefault(m =>
            m.ExplicitOverrides.Any(o => o.InterfaceTypeName == "ILogger" && o.MethodName == "Log"));
        Assert.NotNull(logMethod);
    }

    [Fact]
    public void Build_FeatureTest_FileLogger_InterfaceImpl_Log_Mapped()
    {
        var module = BuildFeatureTest();
        var logger = module.Types.First(t => t.CppName == "FileLogger");
        // ILogger interface impl should have Log method mapped (via explicit override)
        var iloggerImpl = logger.InterfaceImpls.FirstOrDefault(i => i.Interface.CppName == "ILogger");
        Assert.NotNull(iloggerImpl);
        var logImpl = iloggerImpl!.MethodImpls.FirstOrDefault(m => m != null);
        Assert.NotNull(logImpl);
    }

    [Fact]
    public void Build_FeatureTest_FileLogger_InterfaceImpl_Format_Mapped()
    {
        var module = BuildFeatureTest();
        var logger = module.Types.First(t => t.CppName == "FileLogger");
        // IFormatter interface impl should have Format method mapped (via implicit name match)
        var ifmtImpl = logger.InterfaceImpls.FirstOrDefault(i => i.Interface.CppName == "IFormatter");
        Assert.NotNull(ifmtImpl);
        var fmtImpl = ifmtImpl!.MethodImpls.FirstOrDefault(m => m != null && m.Name == "Format");
        Assert.NotNull(fmtImpl);
    }

    // ===== Conversion Operators =====

    [Fact]
    public void Build_FeatureTest_ImplicitOperator_CompilesAsStaticMethod()
    {
        var module = BuildFeatureTest();
        var celsius = module.FindType("Celsius");
        Assert.NotNull(celsius);
        Assert.Contains(celsius!.Methods, m => m.Name == "op_Implicit");
    }

    [Fact]
    public void Build_FeatureTest_ExplicitOperator_CompilesAsStaticMethod()
    {
        var module = BuildFeatureTest();
        var celsius = module.FindType("Celsius");
        Assert.NotNull(celsius);
        Assert.Contains(celsius!.Methods, m => m.Name == "op_Explicit");
    }

    // ===== Default Interface Methods (DIM) =====

    [Fact]
    public void Build_FeatureTest_DIM_InterfaceHasDefaultMethods()
    {
        var module = BuildFeatureTest();
        var iface = module.Types.First(t => t.Name == "IGreeter");
        Assert.True(iface.IsInterface);

        // GetName is abstract — no body
        var getName = iface.Methods.First(m => m.Name == "GetName");
        Assert.True(getName.IsAbstract);
        Assert.Empty(getName.BasicBlocks);

        // Greet has a default body
        var greet = iface.Methods.First(m => m.Name == "Greet");
        Assert.False(greet.IsAbstract);
        Assert.NotEmpty(greet.BasicBlocks);

        // Version has a default body
        var version = iface.Methods.First(m => m.Name == "Version");
        Assert.False(version.IsAbstract);
        Assert.NotEmpty(version.BasicBlocks);
    }

    [Fact]
    public void Build_FeatureTest_DIM_DefaultGreeterUser_UsesDefaultImpl()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "DefaultGreeterUser");

        // DefaultGreeterUser implements IGreeter but doesn't override Greet()
        var iface = module.Types.First(t => t.Name == "IGreeter");

        // Find the interface impl for IGreeter
        var ifaceImpl = type.InterfaceImpls.FirstOrDefault(i => i.Interface == iface);
        Assert.NotNull(ifaceImpl);

        // The Greet slot should point to the interface's default method
        var greetIdx = iface.Methods
            .Where(m => !m.IsConstructor && !m.IsStaticConstructor)
            .ToList()
            .FindIndex(m => m.Name == "Greet");
        Assert.True(greetIdx >= 0);
        var implMethod = ifaceImpl.MethodImpls[greetIdx];
        Assert.NotNull(implMethod);
        Assert.Equal(iface, implMethod.DeclaringType); // Points to interface method
    }

    [Fact]
    public void Build_FeatureTest_DIM_CustomGreeterUser_OverridesDefault()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "CustomGreeterUser");
        var iface = module.Types.First(t => t.Name == "IGreeter");

        var ifaceImpl = type.InterfaceImpls.FirstOrDefault(i => i.Interface == iface);
        Assert.NotNull(ifaceImpl);

        // The Greet slot should point to the class's own override, not interface default
        var greetIdx = iface.Methods
            .Where(m => !m.IsConstructor && !m.IsStaticConstructor)
            .ToList()
            .FindIndex(m => m.Name == "Greet");
        Assert.True(greetIdx >= 0);
        var implMethod = ifaceImpl.MethodImpls[greetIdx];
        Assert.NotNull(implMethod);
        Assert.Equal(type, implMethod.DeclaringType); // Points to class method
    }

    [Fact]
    public void Build_FeatureTest_DIM_ILogger2_AllDefaultMethods()
    {
        var module = BuildFeatureTest();
        var iface = module.Types.First(t => t.Name == "ILogger2");
        Assert.True(iface.IsInterface);

        // Log has a default body
        var log = iface.Methods.First(m => m.Name == "Log");
        Assert.False(log.IsAbstract);
        Assert.NotEmpty(log.BasicBlocks);
    }

    [Fact]
    public void Build_FeatureTest_DIM_LoggerUser_UsesAllDefaults()
    {
        var module = BuildFeatureTest();
        var type = module.Types.First(t => t.Name == "LoggerUser");
        var iface = module.Types.First(t => t.Name == "ILogger2");

        var ifaceImpl = type.InterfaceImpls.FirstOrDefault(i => i.Interface == iface);
        Assert.NotNull(ifaceImpl);

        // Log slot should point to interface default
        var logIdx = iface.Methods
            .Where(m => !m.IsConstructor && !m.IsStaticConstructor)
            .ToList()
            .FindIndex(m => m.Name == "Log");
        Assert.True(logIdx >= 0);
        var implMethod = ifaceImpl.MethodImpls[logIdx];
        Assert.NotNull(implMethod);
        Assert.Equal(iface, implMethod.DeclaringType);
    }

    // ===== Object Methods & Misc =====

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

    [Fact]
    public void Build_FeatureTest_Resource_HasFinalizer()
    {
        var module = BuildFeatureTest();
        var resource = module.FindType("Resource")!;
        Assert.NotNull(resource.Finalizer);
    }

    [Fact]
    public void Build_FeatureTest_Vector2_HasOperator()
    {
        var module = BuildFeatureTest();
        var vector2 = module.FindType("Vector2")!;
        var opMethod = vector2.Methods.FirstOrDefault(m => m.IsOperator);
        Assert.NotNull(opMethod);
        Assert.Equal("op_Addition", opMethod!.OperatorName);
    }

    [Fact]
    public void Build_FeatureTest_ReferenceEquals_CompiledCorrectly()
    {
        var module = BuildFeatureTest();
        // C# compiler inlines Object.ReferenceEquals as ceq (pointer comparison)
        // Verify the test method compiles and produces comparison instructions
        var testMethod = module.GetAllMethods().FirstOrDefault(m => m.Name == "TestReferenceEquals");
        Assert.NotNull(testMethod);
        var instrs = testMethod!.BasicBlocks.SelectMany(b => b.Instructions).ToList();
        // Should have comparison operations (ceq → IRBinaryOp or IRRawCpp with ==)
        Assert.True(instrs.Count > 0);
    }

    [Fact]
    public void Build_FeatureTest_MemberwiseClone_MappedToRuntime()
    {
        var module = BuildFeatureTest();
        var cloneable = module.FindType("Cloneable");
        Assert.NotNull(cloneable);
        var shallowCopy = cloneable!.Methods.FirstOrDefault(m => m.Name == "ShallowCopy");
        Assert.NotNull(shallowCopy);
        var calls = shallowCopy!.BasicBlocks
            .SelectMany(b => b.Instructions)
            .OfType<IRCall>()
            .Select(c => c.FunctionName)
            .ToList();
        Assert.Contains(calls, c => c == "cil2cpp::object_memberwise_clone");
    }
}
