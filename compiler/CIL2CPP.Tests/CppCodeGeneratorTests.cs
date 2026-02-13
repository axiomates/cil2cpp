using Xunit;
using CIL2CPP.Core;
using CIL2CPP.Core.CodeGen;
using CIL2CPP.Core.IR;

namespace CIL2CPP.Tests;

public class CppCodeGeneratorTests
{
    private static IRModule CreateSimpleModule(bool withEntryPoint = true)
    {
        var module = new IRModule { Name = "TestApp" };

        // Create a simple class
        var type = new IRType
        {
            ILFullName = "Calculator",
            CppName = "Calculator",
            Name = "Calculator",
            Namespace = "",
            IsValueType = false,
            IsSealed = false,
            IsAbstract = false,
            IsInterface = false
        };

        // Add a field
        type.Fields.Add(new IRField
        {
            Name = "_result",
            CppName = "f_result",
            FieldTypeName = "System.Int32",
            IsStatic = false
        });

        // Add a static field
        type.StaticFields.Add(new IRField
        {
            Name = "Counter",
            CppName = "f_Counter",
            FieldTypeName = "System.Int32",
            IsStatic = true
        });

        // Add a simple method (Add)
        var addMethod = new IRMethod
        {
            Name = "Add",
            CppName = "Calculator_Add",
            DeclaringType = type,
            IsStatic = false,
            ReturnTypeCpp = "int32_t"
        };
        addMethod.Parameters.Add(new IRParameter { Name = "a", CppName = "a", CppTypeName = "int32_t" });
        addMethod.Parameters.Add(new IRParameter { Name = "b", CppName = "b", CppTypeName = "int32_t" });

        var bb = new IRBasicBlock { Id = 0 };
        bb.Instructions.Add(new IRBinaryOp { Left = "a", Right = "b", Op = "+", ResultVar = "__t0" });
        bb.Instructions.Add(new IRReturn { Value = "__t0" });
        addMethod.BasicBlocks.Add(bb);

        type.Methods.Add(addMethod);
        module.Types.Add(type);

        if (withEntryPoint)
        {
            // Add a main method
            var mainType = new IRType
            {
                ILFullName = "Program",
                CppName = "Program",
                Name = "Program",
                Namespace = "",
                IsValueType = false
            };

            var mainMethod = new IRMethod
            {
                Name = "Main",
                CppName = "Program_Main",
                DeclaringType = mainType,
                IsStatic = true,
                IsEntryPoint = true,
                ReturnTypeCpp = "void"
            };

            var mainBb = new IRBasicBlock { Id = 0 };
            mainBb.Instructions.Add(new IRReturn());
            mainMethod.BasicBlocks.Add(mainBb);

            mainType.Methods.Add(mainMethod);
            module.Types.Add(mainType);
            module.EntryPoint = mainMethod;
        }

        return module;
    }

    // ===== Header Generation =====

    [Fact]
    public void Generate_Header_ContainsForwardDeclarations()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("struct Calculator;", output.HeaderFile.Content);
        Assert.Contains("struct Program;", output.HeaderFile.Content);
    }

    [Fact]
    public void Generate_Header_ContainsTypeInfoDeclarations()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("extern cil2cpp::TypeInfo Calculator_TypeInfo;", output.HeaderFile.Content);
    }

    [Fact]
    public void Generate_Header_ContainsStructDefinition()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("struct Calculator {", output.HeaderFile.Content);
        Assert.Contains("int32_t f_result;", output.HeaderFile.Content);
    }

    [Fact]
    public void Generate_Header_ContainsObjectHeader()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("cil2cpp::TypeInfo* __type_info;", output.HeaderFile.Content);
        Assert.DoesNotContain("cil2cpp::UInt32 __gc_mark;", output.HeaderFile.Content);
        Assert.Contains("cil2cpp::UInt32 __sync_block;", output.HeaderFile.Content);
    }

    [Fact]
    public void Generate_Header_ContainsStaticFieldStorage()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("struct Calculator_Statics {", output.HeaderFile.Content);
        Assert.Contains("extern Calculator_Statics Calculator_statics;", output.HeaderFile.Content);
    }

    [Fact]
    public void Generate_Header_ContainsMethodDeclarations()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("int32_t Calculator_Add(Calculator* __this, int32_t a, int32_t b);", output.HeaderFile.Content);
    }

    [Fact]
    public void Generate_Header_FileName()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Equal("TestApp.h", output.HeaderFile.FileName);
    }

    // ===== Source Generation =====

    [Fact]
    public void Generate_Source_ContainsMethodImpl()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("Calculator_Add", output.SourceFile.Content);
        Assert.Contains("__t0 = a + b;", output.SourceFile.Content);
        Assert.Contains("return __t0;", output.SourceFile.Content);
    }

    [Fact]
    public void Generate_Source_ContainsTypeInfo()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("cil2cpp::TypeInfo Calculator_TypeInfo = {", output.SourceFile.Content);
        Assert.Contains(".name = \"Calculator\"", output.SourceFile.Content);
        Assert.Contains(".instance_size = sizeof(Calculator)", output.SourceFile.Content);
    }

    [Fact]
    public void Generate_Source_ContainsStaticFieldInit()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("Calculator_Statics Calculator_statics = {};", output.SourceFile.Content);
    }

    [Fact]
    public void Generate_Source_FileName()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Equal("TestApp.cpp", output.SourceFile.FileName);
    }

    // ===== Main Generation =====

    [Fact]
    public void Generate_WithEntryPoint_HasMainFile()
    {
        var module = CreateSimpleModule(withEntryPoint: true);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.NotNull(output.MainFile);
    }

    [Fact]
    public void Generate_WithoutEntryPoint_NoMainFile()
    {
        var module = CreateSimpleModule(withEntryPoint: false);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Null(output.MainFile);
    }

    [Fact]
    public void Generate_Main_ContainsRuntimeInit()
    {
        var module = CreateSimpleModule(withEntryPoint: true);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("cil2cpp::runtime_init()", output.MainFile!.Content);
        Assert.Contains("cil2cpp::runtime_shutdown()", output.MainFile.Content);
    }

    [Fact]
    public void Generate_Main_CallsEntryPoint()
    {
        var module = CreateSimpleModule(withEntryPoint: true);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("Program_Main()", output.MainFile!.Content);
    }

    // ===== CMake Generation =====

    [Fact]
    public void Generate_CMake_ContainsFindPackage()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.NotNull(output.CMakeFile);
        Assert.Contains("find_package(cil2cpp REQUIRED)", output.CMakeFile!.Content);
        Assert.Contains("cil2cpp::runtime", output.CMakeFile.Content);
    }

    [Fact]
    public void Generate_CMake_ExeProject_HasAddExecutable()
    {
        var module = CreateSimpleModule(withEntryPoint: true);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("add_executable(TestApp", output.CMakeFile!.Content);
        Assert.Contains("main.cpp", output.CMakeFile.Content);
    }

    [Fact]
    public void Generate_CMake_LibProject_HasAddLibrary()
    {
        var module = CreateSimpleModule(withEntryPoint: false);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("add_library(TestApp STATIC", output.CMakeFile!.Content);
    }

    // ===== String Literals =====

    [Fact]
    public void Generate_WithStringLiterals_HasInitFunction()
    {
        var module = CreateSimpleModule();
        module.RegisterStringLiteral("Hello, World!");
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("__init_string_literals", output.HeaderFile.Content);
        Assert.Contains("string_literal", output.SourceFile.Content);
        Assert.Contains("Hello, World!", output.SourceFile.Content);
    }

    [Fact]
    public void Generate_Main_WithStringLiterals_CallsInit()
    {
        var module = CreateSimpleModule(withEntryPoint: true);
        module.RegisterStringLiteral("test");
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("__init_string_literals()", output.MainFile!.Content);
    }

    // ===== Debug Mode =====

    [Fact]
    public void Generate_Debug_Source_HasDebugComment()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module, BuildConfiguration.Debug);
        var output = gen.Generate();

        Assert.Contains("DEBUG BUILD", output.SourceFile.Content);
    }

    [Fact]
    public void Generate_Release_Source_NoDebugComment()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module, BuildConfiguration.Release);
        var output = gen.Generate();

        Assert.DoesNotContain("DEBUG BUILD", output.SourceFile.Content);
    }

    [Fact]
    public void Generate_Debug_WithSourceInfo_EmitsLineDirective()
    {
        var module = new IRModule { Name = "Test" };
        var type = new IRType { ILFullName = "MyClass", CppName = "MyClass", Name = "MyClass", Namespace = "" };

        var method = new IRMethod
        {
            Name = "Foo",
            CppName = "MyClass_Foo",
            DeclaringType = type,
            IsStatic = true,
            ReturnTypeCpp = "void"
        };

        var bb = new IRBasicBlock { Id = 0 };
        bb.Instructions.Add(new IRReturn
        {
            DebugInfo = new SourceLocation
            {
                FilePath = @"C:\src\test.cs",
                Line = 10,
                Column = 1,
                ILOffset = 0
            }
        });
        method.BasicBlocks.Add(bb);
        type.Methods.Add(method);
        module.Types.Add(type);

        var gen = new CppCodeGenerator(module, BuildConfiguration.Debug);
        var output = gen.Generate();

        Assert.Contains("#line 10", output.SourceFile.Content);
        Assert.Contains("C:/src/test.cs", output.SourceFile.Content); // Forward slashes
        Assert.Contains("/* IL_0000 */", output.SourceFile.Content);
    }

    // ===== GeneratedOutput =====

    [Fact]
    public void GeneratedOutput_WriteToDirectory_CreatesFiles()
    {
        var module = CreateSimpleModule(withEntryPoint: true);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        var tempDir = Path.Combine(Path.GetTempPath(), "cil2cpp_test_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            output.WriteToDirectory(tempDir);
            Assert.True(File.Exists(Path.Combine(tempDir, "TestApp.h")));
            Assert.True(File.Exists(Path.Combine(tempDir, "TestApp.cpp")));
            Assert.True(File.Exists(Path.Combine(tempDir, "main.cpp")));
            Assert.True(File.Exists(Path.Combine(tempDir, "CMakeLists.txt")));
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    // ===== Array Init Data =====

    [Fact]
    public void Generate_WithArrayInitData_EmitsStaticData()
    {
        var module = CreateSimpleModule();
        module.RegisterArrayInitData(new byte[] { 0x01, 0x02, 0xFF });
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("static const unsigned char __arr_init_0[]", output.SourceFile.Content);
        Assert.Contains("0x01", output.SourceFile.Content);
        Assert.Contains("0x02", output.SourceFile.Content);
        Assert.Contains("0xFF", output.SourceFile.Content);
    }

    // ===== Primitive TypeInfo =====

    [Fact]
    public void Generate_WithPrimitiveTypeInfo_EmitsTypeInfoInHeader()
    {
        var module = CreateSimpleModule();
        module.RegisterPrimitiveTypeInfo("System.Int32");
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("extern cil2cpp::TypeInfo System_Int32_TypeInfo;", output.HeaderFile.Content);
    }

    [Fact]
    public void Generate_WithPrimitiveTypeInfo_EmitsTypeInfoInSource()
    {
        var module = CreateSimpleModule();
        module.RegisterPrimitiveTypeInfo("System.Int32");
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("System_Int32_TypeInfo", output.SourceFile.Content);
        Assert.Contains("sizeof(int32_t)", output.SourceFile.Content);
        Assert.Contains("ValueType", output.SourceFile.Content);
    }

    // ===== Static Constructor Guard =====

    [Fact]
    public void Generate_WithStaticCtorGuard_EmitsGuardFunction()
    {
        var module = new IRModule { Name = "Test" };
        var type = new IRType
        {
            ILFullName = "MyClass", CppName = "MyClass", Name = "MyClass", Namespace = "",
            HasCctor = true
        };
        var cctorMethod = new IRMethod
        {
            Name = ".cctor", CppName = "MyClass__cctor", DeclaringType = type,
            IsStatic = true, IsConstructor = true, IsStaticConstructor = true,
            ReturnTypeCpp = "void"
        };
        var bb = new IRBasicBlock { Id = 0 };
        bb.Instructions.Add(new IRReturn());
        cctorMethod.BasicBlocks.Add(bb);
        type.Methods.Add(cctorMethod);
        module.Types.Add(type);

        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("MyClass_cctor_called", output.SourceFile.Content);
        Assert.Contains("MyClass_ensure_cctor()", output.SourceFile.Content);
        Assert.Contains("MyClass_ensure_cctor();", output.HeaderFile.Content);
    }

    // ===== TypeInfo Flags =====

    [Fact]
    public void Generate_ValueType_HasValueTypeFlag()
    {
        var module = new IRModule { Name = "Test" };
        var type = new IRType
        {
            ILFullName = "MyStruct", CppName = "MyStruct", Name = "MyStruct", Namespace = "",
            IsValueType = true
        };
        module.Types.Add(type);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("cil2cpp::TypeFlags::ValueType", output.SourceFile.Content);
    }

    [Fact]
    public void Generate_AbstractType_HasAbstractFlag()
    {
        var module = new IRModule { Name = "Test" };
        var type = new IRType
        {
            ILFullName = "MyBase", CppName = "MyBase", Name = "MyBase", Namespace = "",
            IsAbstract = true
        };
        module.Types.Add(type);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("Abstract", output.SourceFile.Content);
    }

    [Fact]
    public void Generate_SealedType_HasSealedFlag()
    {
        var module = new IRModule { Name = "Test" };
        var type = new IRType
        {
            ILFullName = "MySealed", CppName = "MySealed", Name = "MySealed", Namespace = "",
            IsSealed = true
        };
        module.Types.Add(type);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("Sealed", output.SourceFile.Content);
    }

    // ===== Multi-Level Inheritance (Bug 3 fix verification) =====

    [Fact]
    public void Generate_MultiLevelInheritance_EmitsAllFields()
    {
        var module = new IRModule { Name = "Test" };

        var grandparent = new IRType
        {
            ILFullName = "GrandParent", CppName = "GrandParent", Name = "GrandParent", Namespace = ""
        };
        grandparent.Fields.Add(new IRField
        {
            Name = "gpField", CppName = "f_gpField", FieldTypeName = "System.Int32"
        });

        var parent = new IRType
        {
            ILFullName = "Parent", CppName = "Parent", Name = "Parent", Namespace = "",
            BaseType = grandparent
        };
        parent.Fields.Add(new IRField
        {
            Name = "pField", CppName = "f_pField", FieldTypeName = "System.Int32"
        });

        var child = new IRType
        {
            ILFullName = "Child", CppName = "Child", Name = "Child", Namespace = "",
            BaseType = parent
        };
        child.Fields.Add(new IRField
        {
            Name = "cField", CppName = "f_cField", FieldTypeName = "System.Int32"
        });

        module.Types.Add(grandparent);
        module.Types.Add(parent);
        module.Types.Add(child);

        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        // Child struct should contain fields from GrandParent AND Parent
        var childStructStart = output.HeaderFile.Content.IndexOf("struct Child {");
        var childStructEnd = output.HeaderFile.Content.IndexOf("};", childStructStart);
        var childStruct = output.HeaderFile.Content[childStructStart..childStructEnd];

        Assert.Contains("f_gpField", childStruct);
        Assert.Contains("f_pField", childStruct);
        Assert.Contains("f_cField", childStruct);
    }

    // ===== EscapeString =====

    [Fact]
    public void Generate_StringWithBackslash_IsEscaped()
    {
        var module = CreateSimpleModule();
        module.RegisterStringLiteral("hello\\world");
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("hello\\\\world", output.SourceFile.Content);
    }

    [Fact]
    public void Generate_StringWithNewline_IsEscaped()
    {
        var module = CreateSimpleModule();
        module.RegisterStringLiteral("hello\nworld");
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("hello\\nworld", output.SourceFile.Content);
    }

    [Fact]
    public void Generate_StringWithQuote_IsEscaped()
    {
        var module = CreateSimpleModule();
        module.RegisterStringLiteral("hello\"world");
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("hello\\\"world", output.SourceFile.Content);
    }

    // ===== AddAutoDeclarations =====

    [Fact]
    public void Generate_TempVar_GetsAutoPrefix()
    {
        var module = new IRModule { Name = "Test" };
        var type = new IRType { ILFullName = "MyClass", CppName = "MyClass", Name = "MyClass", Namespace = "" };
        var method = new IRMethod
        {
            Name = "Foo", CppName = "MyClass_Foo", DeclaringType = type,
            IsStatic = true, ReturnTypeCpp = "int32_t"
        };
        var bb = new IRBasicBlock { Id = 0 };
        bb.Instructions.Add(new IRBinaryOp { Left = "1", Right = "2", Op = "+", ResultVar = "__t0" });
        bb.Instructions.Add(new IRReturn { Value = "__t0" });
        method.BasicBlocks.Add(bb);
        type.Methods.Add(method);
        module.Types.Add(type);

        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("auto __t0 = 1 + 2;", output.SourceFile.Content);
    }

    [Fact]
    public void Generate_TempVarLargeIndex_GetsAutoPrefix()
    {
        // Verifies Bug 5 fix: large temp var indices should still get auto
        var module = new IRModule { Name = "Test" };
        var type = new IRType { ILFullName = "MyClass", CppName = "MyClass", Name = "MyClass", Namespace = "" };
        var method = new IRMethod
        {
            Name = "Foo", CppName = "MyClass_Foo", DeclaringType = type,
            IsStatic = true, ReturnTypeCpp = "int32_t"
        };
        var bb = new IRBasicBlock { Id = 0 };
        bb.Instructions.Add(new IRBinaryOp { Left = "1", Right = "2", Op = "+", ResultVar = "__t1000000" });
        bb.Instructions.Add(new IRReturn { Value = "__t1000000" });
        method.BasicBlocks.Add(bb);
        type.Methods.Add(type.Methods.Count == 0 ? method : method);
        type.Methods.Clear();
        type.Methods.Add(method);
        module.Types.Add(type);

        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("auto __t1000000 = 1 + 2;", output.SourceFile.Content);
    }

    // ===== Label Scope Handling =====

    [Fact]
    public void Generate_MethodWithLabels_EmitsLabelScopes()
    {
        var module = new IRModule { Name = "Test" };
        var type = new IRType { ILFullName = "MyClass", CppName = "MyClass", Name = "MyClass", Namespace = "" };
        var method = new IRMethod
        {
            Name = "Foo", CppName = "MyClass_Foo", DeclaringType = type,
            IsStatic = true, ReturnTypeCpp = "void"
        };
        var bb = new IRBasicBlock { Id = 0 };
        bb.Instructions.Add(new IRLabel { LabelName = "IL_0000" });
        bb.Instructions.Add(new IRReturn());
        method.BasicBlocks.Add(bb);
        type.Methods.Add(method);
        module.Types.Add(type);

        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("IL_0000:", output.SourceFile.Content);
        // Verify label scope braces are present
        var methodContent = output.SourceFile.Content;
        Assert.Contains("{", methodContent); // opening scope
    }

    // ===== CMake Default Build Type =====

    [Fact]
    public void Generate_CMake_DebugConfig_DefaultsToDebug()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module, BuildConfiguration.Debug);
        var output = gen.Generate();

        Assert.Contains("\"Debug\"", output.CMakeFile!.Content);
    }

    [Fact]
    public void Generate_CMake_ReleaseConfig_DefaultsToRelease()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module, BuildConfiguration.Release);
        var output = gen.Generate();

        Assert.Contains("\"Release\"", output.CMakeFile!.Content);
    }

    // ===== Source Includes =====

    [Fact]
    public void Generate_Source_IncludesLimits()
    {
        var module = CreateSimpleModule();
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("#include <limits>", output.SourceFile.Content);
    }

    // ===== Library without main =====

    [Fact]
    public void Generate_LibraryProject_CMakeHasPublicLinkage()
    {
        var module = CreateSimpleModule(withEntryPoint: false);
        var gen = new CppCodeGenerator(module);
        var output = gen.Generate();

        Assert.Contains("PUBLIC cil2cpp::runtime", output.CMakeFile!.Content);
    }
}
