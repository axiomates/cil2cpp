using Xunit;
using CIL2CPP.Core.IR;

namespace CIL2CPP.Tests;

public class IRMethodTests
{
    // ===== GetCppSignature =====

    [Fact]
    public void GetCppSignature_StaticVoidNoParams()
    {
        var method = new IRMethod
        {
            Name = "Main",
            CppName = "Program_Main",
            IsStatic = true,
            ReturnTypeCpp = "void"
        };

        Assert.Equal("void Program_Main()", method.GetCppSignature());
    }

    [Fact]
    public void GetCppSignature_StaticWithParams()
    {
        var method = new IRMethod
        {
            Name = "Add",
            CppName = "Calculator_Add",
            IsStatic = true,
            ReturnTypeCpp = "int32_t"
        };
        method.Parameters.Add(new IRParameter { CppName = "a", CppTypeName = "int32_t" });
        method.Parameters.Add(new IRParameter { CppName = "b", CppTypeName = "int32_t" });

        Assert.Equal("int32_t Calculator_Add(int32_t a, int32_t b)", method.GetCppSignature());
    }

    [Fact]
    public void GetCppSignature_InstanceMethod_IncludesThisPointer()
    {
        var type = new IRType { CppName = "Calculator" };
        var method = new IRMethod
        {
            Name = "GetResult",
            CppName = "Calculator_GetResult",
            IsStatic = false,
            DeclaringType = type,
            ReturnTypeCpp = "int32_t"
        };

        Assert.Equal("int32_t Calculator_GetResult(Calculator* __this)", method.GetCppSignature());
    }

    [Fact]
    public void GetCppSignature_InstanceWithParams()
    {
        var type = new IRType { CppName = "Calculator" };
        var method = new IRMethod
        {
            Name = "SetResult",
            CppName = "Calculator_SetResult",
            IsStatic = false,
            DeclaringType = type,
            ReturnTypeCpp = "void"
        };
        method.Parameters.Add(new IRParameter { CppName = "value", CppTypeName = "int32_t" });

        Assert.Equal("void Calculator_SetResult(Calculator* __this, int32_t value)", method.GetCppSignature());
    }

    [Fact]
    public void GetCppSignature_StaticWithManyParams()
    {
        var method = new IRMethod
        {
            Name = "Compute",
            CppName = "Math_Compute",
            IsStatic = true,
            ReturnTypeCpp = "double"
        };
        method.Parameters.Add(new IRParameter { CppName = "a", CppTypeName = "double" });
        method.Parameters.Add(new IRParameter { CppName = "b", CppTypeName = "double" });
        method.Parameters.Add(new IRParameter { CppName = "c", CppTypeName = "int32_t" });

        Assert.Equal("double Math_Compute(double a, double b, int32_t c)", method.GetCppSignature());
    }

    [Fact]
    public void GetCppSignature_InstanceNoDeclaringType_NoThisPointer()
    {
        var method = new IRMethod
        {
            Name = "Orphan",
            CppName = "Orphan",
            IsStatic = false,
            DeclaringType = null,
            ReturnTypeCpp = "void"
        };

        // No declaring type means no __this, even though not static
        Assert.Equal("void Orphan()", method.GetCppSignature());
    }

    [Fact]
    public void GetCppSignature_ReturnsPointerType()
    {
        var method = new IRMethod
        {
            Name = "Create",
            CppName = "Factory_Create",
            IsStatic = true,
            ReturnTypeCpp = "MyClass*"
        };

        Assert.Equal("MyClass* Factory_Create()", method.GetCppSignature());
    }

    [Fact]
    public void GetCppSignature_InstanceWithPointerParams()
    {
        var type = new IRType { CppName = "Processor" };
        var method = new IRMethod
        {
            Name = "Process",
            CppName = "Processor_Process",
            IsStatic = false,
            DeclaringType = type,
            ReturnTypeCpp = "void"
        };
        method.Parameters.Add(new IRParameter { CppName = "input", CppTypeName = "cil2cpp::String*" });
        method.Parameters.Add(new IRParameter { CppName = "output", CppTypeName = "cil2cpp::Array*" });

        Assert.Equal("void Processor_Process(Processor* __this, cil2cpp::String* input, cil2cpp::Array* output)",
            method.GetCppSignature());
    }

    // ===== Property defaults =====

    [Fact]
    public void Defaults_IsStatic_False()
    {
        var method = new IRMethod();
        Assert.False(method.IsStatic);
    }

    [Fact]
    public void Defaults_IsVirtual_False()
    {
        var method = new IRMethod();
        Assert.False(method.IsVirtual);
    }

    [Fact]
    public void Defaults_IsAbstract_False()
    {
        var method = new IRMethod();
        Assert.False(method.IsAbstract);
    }

    [Fact]
    public void Defaults_IsConstructor_False()
    {
        var method = new IRMethod();
        Assert.False(method.IsConstructor);
    }

    [Fact]
    public void Defaults_IsStaticConstructor_False()
    {
        var method = new IRMethod();
        Assert.False(method.IsStaticConstructor);
    }

    [Fact]
    public void Defaults_IsEntryPoint_False()
    {
        var method = new IRMethod();
        Assert.False(method.IsEntryPoint);
    }

    [Fact]
    public void Defaults_IsFinalizer_False()
    {
        var method = new IRMethod();
        Assert.False(method.IsFinalizer);
    }

    [Fact]
    public void Defaults_IsOperator_False()
    {
        var method = new IRMethod();
        Assert.False(method.IsOperator);
    }

    [Fact]
    public void Defaults_VTableSlot_MinusOne()
    {
        var method = new IRMethod();
        Assert.Equal(-1, method.VTableSlot);
    }

    [Fact]
    public void Defaults_ReturnTypeCpp_Void()
    {
        var method = new IRMethod();
        Assert.Equal("void", method.ReturnTypeCpp);
    }

    [Fact]
    public void Defaults_Collections_Empty()
    {
        var method = new IRMethod();
        Assert.Empty(method.Parameters);
        Assert.Empty(method.Locals);
        Assert.Empty(method.BasicBlocks);
    }

    [Fact]
    public void Defaults_OperatorName_Null()
    {
        var method = new IRMethod();
        Assert.Null(method.OperatorName);
    }

    // ===== IRParameter =====

    [Fact]
    public void IRParameter_Defaults()
    {
        var param = new IRParameter();
        Assert.Equal("", param.Name);
        Assert.Equal("", param.CppName);
        Assert.Null(param.ParameterType);
        Assert.Equal("", param.CppTypeName);
        Assert.Equal(0, param.Index);
    }

    [Fact]
    public void IRParameter_SetProperties()
    {
        var type = new IRType { CppName = "MyType" };
        var param = new IRParameter
        {
            Name = "arg",
            CppName = "p_arg",
            ParameterType = type,
            CppTypeName = "MyType*",
            Index = 2
        };
        Assert.Equal("arg", param.Name);
        Assert.Equal("p_arg", param.CppName);
        Assert.Same(type, param.ParameterType);
        Assert.Equal("MyType*", param.CppTypeName);
        Assert.Equal(2, param.Index);
    }

    // ===== IRLocal =====

    [Fact]
    public void IRLocal_Defaults()
    {
        var local = new IRLocal();
        Assert.Equal(0, local.Index);
        Assert.Equal("", local.CppName);
        Assert.Null(local.LocalType);
        Assert.Equal("", local.CppTypeName);
    }

    [Fact]
    public void IRLocal_SetProperties()
    {
        var type = new IRType { CppName = "int32_t" };
        var local = new IRLocal
        {
            Index = 3,
            CppName = "__loc3",
            LocalType = type,
            CppTypeName = "int32_t"
        };
        Assert.Equal(3, local.Index);
        Assert.Equal("__loc3", local.CppName);
        Assert.Same(type, local.LocalType);
    }

    // ===== IRBasicBlock =====

    [Fact]
    public void IRBasicBlock_Label_CorrectFormat()
    {
        Assert.Equal("BB_0", new IRBasicBlock { Id = 0 }.Label);
        Assert.Equal("BB_42", new IRBasicBlock { Id = 42 }.Label);
        Assert.Equal("BB_100", new IRBasicBlock { Id = 100 }.Label);
    }

    [Fact]
    public void IRBasicBlock_Instructions_InitEmpty()
    {
        var bb = new IRBasicBlock();
        Assert.Empty(bb.Instructions);
    }

    [Fact]
    public void IRBasicBlock_Instructions_AddMultiple()
    {
        var bb = new IRBasicBlock { Id = 0 };
        bb.Instructions.Add(new IRComment { Text = "first" });
        bb.Instructions.Add(new IRAssign { Target = "x", Value = "1" });
        bb.Instructions.Add(new IRReturn());
        Assert.Equal(3, bb.Instructions.Count);
    }

    // ===== SourceLocation =====

    [Fact]
    public void SourceLocation_DefaultILOffset_MinusOne()
    {
        var loc = new SourceLocation { FilePath = "test.cs", Line = 1 };
        Assert.Equal(-1, loc.ILOffset);
    }

    [Fact]
    public void SourceLocation_Equality_SameValues()
    {
        var a = new SourceLocation { FilePath = "a.cs", Line = 10, Column = 5, ILOffset = 0 };
        var b = new SourceLocation { FilePath = "a.cs", Line = 10, Column = 5, ILOffset = 0 };
        Assert.Equal(a, b);
    }

    [Fact]
    public void SourceLocation_Equality_DifferentValues()
    {
        var a = new SourceLocation { FilePath = "a.cs", Line = 10 };
        var b = new SourceLocation { FilePath = "b.cs", Line = 10 };
        Assert.NotEqual(a, b);
    }
}

// ===== IRType Tests =====

public class IRTypeTests
{
    // ===== GetCppTypeName =====

    [Fact]
    public void GetCppTypeName_ValueType_NoBarePointer()
    {
        var type = new IRType { CppName = "MyStruct", IsValueType = true };
        Assert.Equal("MyStruct", type.GetCppTypeName());
    }

    [Fact]
    public void GetCppTypeName_ValueType_AsPointer()
    {
        var type = new IRType { CppName = "MyStruct", IsValueType = true };
        Assert.Equal("MyStruct*", type.GetCppTypeName(asPointer: true));
    }

    [Fact]
    public void GetCppTypeName_ReferenceType_AlwaysPointer()
    {
        var type = new IRType { CppName = "MyClass", IsValueType = false };
        Assert.Equal("MyClass*", type.GetCppTypeName());
    }

    [Fact]
    public void GetCppTypeName_ReferenceType_AsPointer_StillPointer()
    {
        var type = new IRType { CppName = "MyClass", IsValueType = false };
        Assert.Equal("MyClass*", type.GetCppTypeName(asPointer: true));
    }

    [Fact]
    public void GetCppTypeName_EnumType_IsValueType()
    {
        var type = new IRType { CppName = "MyEnum", IsValueType = true, IsEnum = true };
        Assert.Equal("MyEnum", type.GetCppTypeName());
    }

    [Fact]
    public void GetCppTypeName_InterfaceType_IsPointer()
    {
        var type = new IRType { CppName = "IMyInterface", IsValueType = false, IsInterface = true };
        Assert.Equal("IMyInterface*", type.GetCppTypeName());
    }

    [Fact]
    public void GetCppTypeName_DelegateType_IsPointer()
    {
        var type = new IRType { CppName = "MyDelegate", IsValueType = false, IsDelegate = true };
        Assert.Equal("MyDelegate*", type.GetCppTypeName());
    }

    // ===== Property defaults =====

    [Fact]
    public void Defaults_AllBooleansAreFalse()
    {
        var type = new IRType();
        Assert.False(type.IsValueType);
        Assert.False(type.IsInterface);
        Assert.False(type.IsAbstract);
        Assert.False(type.IsSealed);
        Assert.False(type.IsEnum);
        Assert.False(type.HasCctor);
        Assert.False(type.IsDelegate);
        Assert.False(type.IsGenericInstance);
    }

    [Fact]
    public void Defaults_StringsAreEmpty()
    {
        var type = new IRType();
        Assert.Equal("", type.ILFullName);
        Assert.Equal("", type.CppName);
        Assert.Equal("", type.Name);
        Assert.Equal("", type.Namespace);
    }

    [Fact]
    public void Defaults_NullablePropertiesAreNull()
    {
        var type = new IRType();
        Assert.Null(type.BaseType);
        Assert.Null(type.EnumUnderlyingType);
        Assert.Null(type.Finalizer);
    }

    [Fact]
    public void Defaults_CollectionsAreEmpty()
    {
        var type = new IRType();
        Assert.Empty(type.Interfaces);
        Assert.Empty(type.Fields);
        Assert.Empty(type.StaticFields);
        Assert.Empty(type.Methods);
        Assert.Empty(type.VTable);
        Assert.Empty(type.InterfaceImpls);
        Assert.Empty(type.GenericArguments);
    }

    [Fact]
    public void Defaults_InstanceSize_Zero()
    {
        var type = new IRType();
        Assert.Equal(0, type.InstanceSize);
    }

    // ===== Collections manipulation =====

    [Fact]
    public void Fields_AddAndRetrieve()
    {
        var type = new IRType { CppName = "MyClass" };
        var field = new IRField { Name = "x", CppName = "f_x", FieldTypeName = "int32_t" };
        type.Fields.Add(field);
        Assert.Single(type.Fields);
        Assert.Equal("f_x", type.Fields[0].CppName);
    }

    [Fact]
    public void Methods_AddAndRetrieve()
    {
        var type = new IRType { CppName = "MyClass" };
        type.Methods.Add(new IRMethod { Name = "Foo", CppName = "MyClass_Foo" });
        type.Methods.Add(new IRMethod { Name = "Bar", CppName = "MyClass_Bar" });
        Assert.Equal(2, type.Methods.Count);
    }

    [Fact]
    public void VTable_AddAndRetrieve()
    {
        var type = new IRType { CppName = "MyClass" };
        var entry = new IRVTableEntry { Slot = 0, MethodName = "ToString" };
        type.VTable.Add(entry);
        Assert.Single(type.VTable);
        Assert.Equal(0, type.VTable[0].Slot);
    }

    [Fact]
    public void InterfaceImpls_AddAndRetrieve()
    {
        var iface = new IRType { CppName = "IMyInterface", IsInterface = true };
        var type = new IRType { CppName = "MyClass" };
        var impl = new IRInterfaceImpl { Interface = iface };
        impl.MethodImpls.Add(new IRMethod { CppName = "MyClass_Foo" });
        type.InterfaceImpls.Add(impl);
        Assert.Single(type.InterfaceImpls);
        Assert.Same(iface, type.InterfaceImpls[0].Interface);
    }

    [Fact]
    public void Interfaces_AddMultiple()
    {
        var type = new IRType { CppName = "MyClass" };
        type.Interfaces.Add(new IRType { CppName = "IFoo", IsInterface = true });
        type.Interfaces.Add(new IRType { CppName = "IBar", IsInterface = true });
        Assert.Equal(2, type.Interfaces.Count);
    }

    [Fact]
    public void GenericArguments_SetAndRetrieve()
    {
        var type = new IRType
        {
            CppName = "Wrapper_1_System_Int32",
            IsGenericInstance = true
        };
        type.GenericArguments.Add("System.Int32");
        Assert.Single(type.GenericArguments);
        Assert.Equal("System.Int32", type.GenericArguments[0]);
    }

    [Fact]
    public void GenericArguments_MultipleArgs()
    {
        var type = new IRType
        {
            CppName = "Dict_2_System_String_System_Int32",
            IsGenericInstance = true
        };
        type.GenericArguments.Add("System.String");
        type.GenericArguments.Add("System.Int32");
        Assert.Equal(2, type.GenericArguments.Count);
    }

    // ===== BaseType chain =====

    [Fact]
    public void BaseType_SingleLevel()
    {
        var baseType = new IRType { CppName = "Base", ILFullName = "Base" };
        var derived = new IRType { CppName = "Derived", ILFullName = "Derived", BaseType = baseType };
        Assert.Same(baseType, derived.BaseType);
    }

    [Fact]
    public void BaseType_MultiLevel()
    {
        var grandParent = new IRType { CppName = "A" };
        var parent = new IRType { CppName = "B", BaseType = grandParent };
        var child = new IRType { CppName = "C", BaseType = parent };
        Assert.Same(parent, child.BaseType);
        Assert.Same(grandParent, child.BaseType!.BaseType);
    }

    // ===== Enum-specific =====

    [Fact]
    public void Enum_UnderlyingType()
    {
        var type = new IRType { CppName = "MyEnum", IsEnum = true, IsValueType = true, EnumUnderlyingType = "System.Int32" };
        Assert.Equal("System.Int32", type.EnumUnderlyingType);
    }

    // ===== Finalizer =====

    [Fact]
    public void Finalizer_SetAndRetrieve()
    {
        var type = new IRType { CppName = "MyClass" };
        var fin = new IRMethod { Name = "Finalize", CppName = "MyClass_Finalize", IsFinalizer = true };
        type.Finalizer = fin;
        Assert.Same(fin, type.Finalizer);
        Assert.True(type.Finalizer.IsFinalizer);
    }
}

// ===== IRField Tests =====

public class IRFieldTests
{
    [Fact]
    public void Defaults()
    {
        var field = new IRField();
        Assert.Equal("", field.Name);
        Assert.Equal("", field.CppName);
        Assert.Null(field.FieldType);
        Assert.Equal("", field.FieldTypeName);
        Assert.False(field.IsStatic);
        Assert.False(field.IsPublic);
        Assert.Equal(0, field.Offset);
        Assert.Null(field.DeclaringType);
        Assert.Null(field.ConstantValue);
    }

    [Fact]
    public void ConstantValue_Int()
    {
        var field = new IRField { Name = "MaxValue", ConstantValue = 100 };
        Assert.Equal(100, field.ConstantValue);
    }

    [Fact]
    public void ConstantValue_String()
    {
        var field = new IRField { Name = "DefaultName", ConstantValue = "hello" };
        Assert.Equal("hello", field.ConstantValue);
    }
}

// ===== IRVTableEntry Tests =====

public class IRVTableEntryTests
{
    [Fact]
    public void Defaults()
    {
        var entry = new IRVTableEntry();
        Assert.Equal(0, entry.Slot);
        Assert.Equal("", entry.MethodName);
        Assert.Null(entry.Method);
        Assert.Null(entry.DeclaringType);
    }

    [Fact]
    public void SetProperties()
    {
        var type = new IRType { CppName = "MyClass" };
        var method = new IRMethod { CppName = "MyClass_ToString" };
        var entry = new IRVTableEntry
        {
            Slot = 2,
            MethodName = "ToString",
            Method = method,
            DeclaringType = type
        };
        Assert.Equal(2, entry.Slot);
        Assert.Same(method, entry.Method);
        Assert.Same(type, entry.DeclaringType);
    }
}

// ===== IRInterfaceImpl Tests =====

public class IRInterfaceImplTests
{
    [Fact]
    public void MethodImpls_InitEmpty()
    {
        var impl = new IRInterfaceImpl { Interface = new IRType { CppName = "IFoo" } };
        Assert.Empty(impl.MethodImpls);
    }

    [Fact]
    public void MethodImpls_AddMultiple()
    {
        var iface = new IRType { CppName = "IFoo", IsInterface = true };
        var impl = new IRInterfaceImpl { Interface = iface };
        impl.MethodImpls.Add(new IRMethod { CppName = "Impl_Method1" });
        impl.MethodImpls.Add(new IRMethod { CppName = "Impl_Method2" });
        impl.MethodImpls.Add(null); // slot can be null if not implemented
        Assert.Equal(3, impl.MethodImpls.Count);
        Assert.Null(impl.MethodImpls[2]);
    }
}
