using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Base class with virtual methods (exercises callvirt, vtable)
public class Animal
{
    private static int _count;
    protected string _name;

    public Animal(string name)
    {
        _name = name;
        _count++;
    }

    public virtual string Speak()
    {
        return "...";
    }

    public static int GetCount()
    {
        return _count;
    }
}

// Derived class with override (exercises vtable override)
public class Dog : Animal
{
    public Dog(string name) : base(name) { }

    public override string Speak()
    {
        return "Woof!";
    }
}

// Another derived for casting tests
public class Cat : Animal
{
    public Cat(string name) : base(name) { }

    public override string Speak()
    {
        return "Meow!";
    }
}

// Enum type (exercises IsEnum, value type)
public enum Color
{
    Red = 0,
    Green = 1,
    Blue = 2
}

// Value type / struct
public struct Point
{
    public int X;
    public int Y;
}

// Interface for testing interface dispatch
public interface ISpeak
{
    string GetSound();
}

// Derived class implementing an interface
public class Duck : Animal, ISpeak
{
    public Duck(string name) : base(name) { }

    public override string Speak()
    {
        return "Quack!";
    }

    public string GetSound()
    {
        return "Quack quack!";
    }
}

// Class with finalizer
public class Resource
{
    private string _name;

    public Resource(string name)
    {
        _name = name;
    }

    ~Resource()
    {
        // Destructor / Finalize
    }

    public string GetName()
    {
        return _name;
    }
}

// Value type with operator overloading
public struct Vector2
{
    public float X;
    public float Y;

    public Vector2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static Vector2 operator +(Vector2 a, Vector2 b)
    {
        return new Vector2(a.X + b.X, a.Y + b.Y);
    }
}

// Properties (auto-properties + manual properties)
public class Person
{
    public string Name { get; set; }
    public int Age { get; set; }

    private int _manualProp;
    public int ManualProp
    {
        get { return _manualProp; }
        set { _manualProp = value * 2; }
    }

    public Person(string name, int age)
    {
        Name = name;
        Age = age;
    }
}

// IDisposable for using statements
public interface IMyDisposable
{
    void Dispose();
}

public class DisposableResource : IMyDisposable
{
    private string _name;
    private bool _disposed;

    public DisposableResource(string name)
    {
        _name = name;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Console.WriteLine(string.Concat("Disposed: ", _name));
            _disposed = true;
        }
    }
}

// Real IDisposable implementation (BCL interface proxy)
public class ManagedResource : IDisposable
{
    private string _name;
    private bool _disposed;

    public ManagedResource(string name)
    {
        _name = name;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Console.WriteLine(string.Concat("Disposed managed: ", _name));
            _disposed = true;
        }
    }
}

// IComparable implementation (BCL interface proxy)
public class Priority : IComparable
{
    public int Value;

    public Priority(int value) { Value = value; }

    public int CompareTo(object? other)
    {
        if (other is Priority p)
            return Value - p.Value;
        return 0;
    }
}

// Delegate types
public delegate int MathOp(int a, int b);
public delegate void VoidAction();
public delegate int IntFunc(int x);
public delegate void NotifyDelegate(int value);
public delegate string StringFunc();

// Generic class
public class Wrapper<T>
{
    private T _value;

    public Wrapper(T value)
    {
        _value = value;
    }

    public T GetValue()
    {
        return _value;
    }

    public void SetValue(T value)
    {
        _value = value;
    }
}

// Generic class with array, ref param, and boxing operations (exercises generic type resolution)
public class GenericHelper<T>
{
    private T[] _items;

    public GenericHelper(int size)
    {
        _items = new T[size]; // newarr T (generic type in array creation)
    }

    public void Set(int index, T value)
    {
        _items[index] = value; // stelem with generic type
    }

    public T Get(int index)
    {
        return _items[index]; // ldelem with generic type
    }

    public int Count()
    {
        return _items.Length;
    }

    // Exercises ByReferenceType(GenericParameter) in ResolveGenericTypeName
    public void Swap(ref T a, ref T b)
    {
        T temp = a;
        a = b;
        b = temp;
    }
}

// Class with readonly field (IsInitOnly)
public class Config
{
    public readonly int MaxRetries = 3;
    public readonly string Name;

    public Config(string name)
    {
        Name = name;
    }
}

// Event source class (exercises event add/remove + delegate invoke)
public class EventSource
{
    public event NotifyDelegate OnNotify;

    public void Subscribe(NotifyDelegate handler)
    {
        OnNotify += handler;
    }

    public void Unsubscribe(NotifyDelegate handler)
    {
        OnNotify -= handler;
    }

    public void Fire(int value)
    {
        if (OnNotify != null)
            OnNotify(value);
    }
}

// Utility class with generic METHODS (not generic class — exercises GenericInstanceMethod)
public class GenericUtils
{
    public static T Identity<T>(T value)
    {
        return value;
    }

    public static void Swap<T>(ref T a, ref T b)
    {
        T temp = a;
        a = b;
        b = temp;
    }
}

// Indexer class (exercises get_Item/set_Item ordinary method calls)
public class IntList
{
    private int[] _items;

    public IntList(int size)
    {
        _items = new int[size];
    }

    public int this[int index]
    {
        get { return _items[index]; }
        set { _items[index] = value; }
    }

    public int Count => _items.Length;
}

// Init-only setter (exercises modreq(IsExternalInit) which CIL2CPP ignores)
public class ImmutablePoint
{
    public int X { get; init; }
    public int Y { get; init; }
}

// Default parameter helper (C# compiler fills defaults at call site in IL)
public class DefaultParamHelper
{
    public static int Add(int a, int b = 10)
    {
        return a + b;
    }

    public static string Greet(string name, string greeting = "Hello")
    {
        return string.Concat(greeting, ", ", name);
    }
}

// Generic value type (exercises RegisterValueType for generic instances)
public struct Pair<T>
{
    public T First;
    public T Second;

    public Pair(T first, T second)
    {
        First = first;
        Second = second;
    }
}

// Class with all field size types (exercises GetFieldSize for Int16/Char/Int64/Double)
public class AllFieldTypes
{
    public short ShortField;
    public char CharField;
    public long LongField;
    public double DoubleField;
    public byte ByteField;
    public float FloatField;
}

// Record type (exercises compiler-generated ToString, Equals, GetHashCode, <Clone>$)
public record PersonRecord(string Name, int Age);

// Abstract class with multi-level inheritance chain (exercises abstract methods + vtable dispatch)
public abstract class Shape
{
    public abstract double Area();
    public virtual string Describe() => "Shape";
}

public class Circle : Shape
{
    public double Radius;
    public Circle(double r) { Radius = r; }
    public override double Area() => 3.14159 * Radius * Radius;
    public override string Describe() => "Circle";
}

public class UnitCircle : Circle
{
    public UnitCircle() : base(1.0) { }
    // Inherits Area() and Describe() from Circle
}

// Value type with implicit/explicit conversion operators
public struct Celsius
{
    public double Value;
    public Celsius(double v) { Value = v; }
    public static implicit operator double(Celsius c) => c.Value;
    public static explicit operator Celsius(double d) => new Celsius(d);
}

// Class that exposes MemberwiseClone (protected on System.Object)
public class Cloneable
{
    public int Value;
    public Cloneable(int v) { Value = v; }
    public Cloneable ShallowCopy() => (Cloneable)MemberwiseClone();
}

// Overloaded virtual methods with same name but different param types (vtable correctness)
public class Formatter
{
    public virtual string Format(int x) => x.ToString();
    public virtual string Format(string s) => s;
}

public class PrefixFormatter : Formatter
{
    public override string Format(int x) => x.ToString();
    public override string Format(string s) => s;
}

// Class with static constructor triggered by static method call (ECMA-335 II.10.5.3.1)
public class LazyInit
{
    private static int _value;
    static LazyInit() { _value = 42; }
    public static int GetValue() => _value;
}

// Record struct (value type record — no <Clone>$, uses value copy semantics)
public record struct PointRecord(int X, int Y);

// Interface inheritance: IB : IA (exercises flattened interface list in Cecil metadata)
public interface IBase
{
    string BaseMethod();
}

public interface IDerived : IBase
{
    string DerivedMethod();
}

public class InterfaceInheritImpl : IDerived
{
    public string BaseMethod() => "base";
    public string DerivedMethod() => "derived";
}

// Overloaded interface methods: same name, different parameter types
public interface IProcess
{
    void Process(int x);
    void Process(string s);
}

public class Processor : IProcess
{
    public int LastInt;
    public string LastString;

    public void Process(int x) { LastInt = x; }
    public void Process(string s) { LastString = s; }
}

// Multi-parameter generic type (exercises >1 generic param substitution)
public class KeyValue<K, V>
{
    public K Key;
    public V Value;
    public KeyValue(K key, V value) { Key = key; Value = value; }
}

// Generic type inheriting from another generic type (exercises BaseType resolution)
public class SpecialWrapper<T> : Wrapper<T>
{
    public string Tag;
    public SpecialWrapper(T value, string tag) : base(value) { Tag = tag; }
}

// Generic type with static constructor (exercises HasCctor per specialization)
public class GenericCache<T>
{
    private static int _initCount;
    static GenericCache() { _initCount++; }
    public static int GetInitCount() => _initCount;
    public T Item;
    public GenericCache(T item) { Item = item; }
}

// ===== Explicit Interface Implementation =====
public interface ILogger
{
    void Log(string message);
}

public interface IFormatter
{
    string Format(int value);
}

// Implements ILogger explicitly: void ILogger.Log() has mangled name in IL
public class FileLogger : ILogger, IFormatter
{
    public int LogCount;

    void ILogger.Log(string message)
    {
        LogCount++;
    }

    // Implicit implementation of IFormatter
    public string Format(int value)
    {
        return value.ToString();
    }
}

// ===== Method Hiding (newslot / new keyword) =====
public class BaseDisplay
{
    public virtual string Show() => "BaseDisplay";
    public virtual int Value() => 1;
}

public class DerivedDisplay : BaseDisplay
{
    // 'new virtual' hides base Show() — creates new vtable slot
    public new virtual string Show() => "DerivedDisplay";
    // Normal override — reuses base vtable slot
    public override int Value() => 2;
}

public class FinalDisplay : DerivedDisplay
{
    // Overrides DerivedDisplay.Show (the hidden slot)
    public override string Show() => "FinalDisplay";
}

// ===== sizeof test =====
public struct TinyStruct
{
    public byte A;
}

public struct BigStruct
{
    public long X;
    public long Y;
    public long Z;
}

// Helper class for Index/Range tests with GetOffset usage
public class IndexRangeHelper
{
    // Exercises explicit Index constructor and GetOffset
    public static int GetFromEnd(int[] arr, int fromEndValue)
    {
        var idx = new Index(fromEndValue, true);
        int offset = idx.GetOffset(arr.Length);
        return arr[offset];
    }

    // Exercises Range.GetOffsetAndLength
    public static int SliceLength(int totalLength, Range range)
    {
        var (offset, length) = range.GetOffsetAndLength(totalLength);
        return length;
    }
}

// Custom exception types (Phase 8.2 — exercises user-defined exception throw/catch)
public class CustomException : Exception
{
    public int ErrorCode { get; }
    public CustomException(string message, int errorCode) : base(message)
    {
        ErrorCode = errorCode;
    }
}

public class DerivedCustomException : CustomException
{
    public DerivedCustomException(string message) : base(message, 999) { }
}

public class Program
{
    // Static field (exercises ldsfld/stsfld)
    private static int _globalValue = 100;

    public static void Main()
    {
        TestArithmetic();
        TestBranching();
        TestConversions();
        TestBitwiseOps();
        TestExceptionHandling();
        TestCustomExceptions();
        TestVirtualCalls();
        TestCasting();
        TestBoxingUnboxing();
        TestSwitchStatement();
        TestFloatDouble();
        TestLong();
        TestStaticFields();
        TestNullAndDup();
        TestStringOps("World");
        TestObjectMethods();
        TestMathOps();
        TestStructOps();
        TestMoreConversions();
        ModifyArg(5);
        Console.WriteLine(ManyParams(1, 2, 3, 4, 5, 6));
        TestConsoleWrite();
        TestMoreMathOps();
        TestConstants();
        TestReadonlyField();
        TestInterfaceDispatch();
        TestFinalizer();
        TestOperator();
        TestProperties();
        TestForeachArray();
        TestUsing();
        TestUsingStatement();
        TestDelegate();
        TestGenerics();
        TestRethrow();
        TestSpecialFloats();
        TestDelegateCombine();
        TestMathAbsOverloads();
        TestAllFieldTypes();
        TestVirtualObjectDispatch();
        TestTypedArrays();
        TestGenericHelper();
        TestLambda();
        TestClosure();
        TestEvents();
        TestGenericMethods();
        TestRefParams();
        TestStaticFieldRef();
        TestVirtualDelegate();
        TestGenericStruct();
        TestIndexer();
        TestDefaultParameters();
        TestInitOnlySetter();
        TestCheckedOverflow();
        TestNullable();
        TestNullableBoxing();
        TestValueTuple();
        TestValueTupleEquals();
        TestUnboxAnyRefType();
        TestRecord();
        TestRecordStruct();
        TestAbstractInheritance();
        TestConversionOperators();
        TestStaticMethodCctor();
        TestReferenceEquals();
        TestMemberwiseClone();
        TestOverloadedVirtual();
        TestInterfaceInheritance();
        TestOverloadedInterface();
        TestMultiParamGeneric();
        TestGenericInheritance();
        TestNestedGeneric();
        TestGenericCctor();
        TestExplicitInterface();
        TestMethodHiding();
        TestSizeOf();
        TestWhileLoop();
        TestDoWhileLoop();
        TestForLoop();
        TestNestedLoopBreakContinue();
        TestGoto();
        TestNestedIfElse();
        TestTernary();
        TestShortCircuit();
        TestUnsignedComparison();
        TestFloatNaNComparison();
        TestAsync();
        TestIndexFromEnd();
        TestRangeSlice();
        TestStringSlice();
        TestIndexProperties();
        TestRangeGetOffsetAndLength();
        TestTypeof();
        TestGetType();
        TestTypeEquality();
        TestTypeHierarchy();
        TestTypeToString();
        TestGetTypeOnValueType();
        TestListInt();
        TestListString();
        TestDictionaryStringInt();
        TestAsyncConcurrency();
        TestAsyncEnumerable();
        TestReflectionAdvanced();
        TestNestedExceptionHandling();
    }

    static void TestAsyncEnumerable()
    {
        // await foreach with async iterator
        var sumTask = AsyncEnumerableHelper.SumRangeAsync();
        sumTask.Wait();
        Console.WriteLine(sumTask.Result); // 15

        var joinTask = AsyncEnumerableHelper.JoinFilteredAsync();
        joinTask.Wait();
        Console.WriteLine(joinTask.Result); // 5,8,7
    }

    static void TestReflectionAdvanced()
    {
        // Test Type.GetMethods()
        var dogType = typeof(Dog);
        var methods = dogType.GetMethods();
        Console.WriteLine(methods.Length > 0); // True — has at least .ctor and Speak

        // Test Type.GetMethod(string)
        var speakMethod = dogType.GetMethod("Speak");
        Console.WriteLine(speakMethod != null); // True
        Console.WriteLine(speakMethod.Name); // Speak
        Console.WriteLine(speakMethod.IsPublic); // True
        Console.WriteLine(speakMethod.IsVirtual); // True
        Console.WriteLine(speakMethod.IsStatic); // False

        // Test Type.GetFields()
        var animalType = typeof(Animal);
        var fields = animalType.GetFields();
        // Animal has _name (protected) and _count (private static) — field count depends on codegen
        Console.WriteLine(fields != null); // True

        // Test Type.GetField(string)
        var nameField = animalType.GetField("_name");
        if (nameField != null)
        {
            Console.WriteLine(nameField.Name); // _name
            Console.WriteLine(nameField.IsStatic); // False
        }

        // Test MethodInfo.DeclaringType
        if (speakMethod != null)
        {
            var declType = speakMethod.DeclaringType;
            Console.WriteLine(declType.Name); // Dog
        }

        // Test MethodInfo.ToString()
        if (speakMethod != null)
        {
            var str = speakMethod.ToString();
            Console.WriteLine(str.Length > 0); // True
        }

        // Test MethodInfo.GetParameters()
        var ctorMethod = animalType.GetMethod(".ctor");
        if (ctorMethod != null)
        {
            var parms = ctorMethod.GetParameters();
            Console.WriteLine(parms.Length); // 1 (String name)
        }
    }

    static void TestNestedExceptionHandling()
    {
        // Test 1: try-catch nested in try-finally
        try
        {
            try
            {
                throw new InvalidOperationException("inner");
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("Inner catch");
            }
        }
        finally
        {
            Console.WriteLine("Outer finally");
        }

        // Test 2: try-finally nested in try-catch
        try
        {
            try
            {
                throw new InvalidOperationException("inner");
            }
            finally
            {
                Console.WriteLine("Inner finally");
            }
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("Outer catch");
        }

        // Test 3: three-layer nesting (catch throws different type to outer)
        try
        {
            try
            {
                try
                {
                    throw new Exception("deep");
                }
                catch (Exception)
                {
                    Console.WriteLine("Level 3 catch");
                    throw new InvalidOperationException("from-catch");
                }
            }
            catch (InvalidOperationException)
            {
                Console.WriteLine("Level 2 catch");
            }
        }
        finally
        {
            Console.WriteLine("Level 1 finally");
        }

        // Test 4: catch rethrow + outer catch
        try
        {
            try
            {
                throw new Exception("original");
            }
            catch (Exception)
            {
                Console.WriteLine("Before rethrow");
                throw;
            }
        }
        catch (Exception)
        {
            Console.WriteLine("After rethrow");
        }

        // Test 5: finally runs during exception propagation
        try
        {
            try
            {
                throw new Exception("propagating");
            }
            finally
            {
                Console.WriteLine("Finally during propagation");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Caught: " + ex.Message);
        }

        // Test 6: exception from finally replaces original
        try
        {
            try
            {
                throw new Exception("original");
            }
            finally
            {
                Console.WriteLine("Finally before new throw");
                throw new InvalidOperationException("from-finally");
            }
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine("Caught from finally: " + ex.Message);
        }
    }

    static void TestArithmetic()
    {
        int a = 10;
        int b = 3;
        int sum = a + b;       // add
        int diff = a - b;      // sub
        int prod = a * b;      // mul
        int quot = a / b;      // div
        int rem = a % b;       // rem
        int neg = -a;          // neg
        Console.WriteLine(sum);
        Console.WriteLine(neg);
    }

    static void TestBranching()
    {
        int x = 5;

        // Comparison branches (beq, bne, bgt, blt, bge, ble)
        if (x == 5)
            Console.WriteLine("eq");
        if (x != 3)
            Console.WriteLine("ne");
        if (x > 3)
            Console.WriteLine("gt");
        if (x < 10)
            Console.WriteLine("lt");
        if (x >= 5)
            Console.WriteLine("ge");
        if (x <= 5)
            Console.WriteLine("le");

        // Comparison operators (ceq, cgt, clt)
        bool isEqual = (x == 5);
        bool isGreater = (x > 0);
        bool isLess = (x < 100);
        Console.WriteLine(isEqual);
        Console.WriteLine(isGreater);

        // Brtrue/brfalse
        if (isEqual)
            Console.WriteLine("true branch");
    }

    static void TestConversions()
    {
        int i = 42;
        long l = (long)i;       // conv.i8
        short s = (short)i;     // conv.i2
        byte b = (byte)i;       // conv.u1
        float f = (float)i;     // conv.r4
        double d = (double)i;   // conv.r8
        int back = (int)l;      // conv.i4
        Console.WriteLine(l);
        Console.WriteLine(d);
    }

    static void TestBitwiseOps()
    {
        int a = 0xFF;
        int b = 0x0F;
        int andResult = a & b;   // and
        int orResult = a | b;    // or
        int xorResult = a ^ b;   // xor
        int notResult = ~a;      // not
        int shlResult = b << 2;  // shl
        int shrResult = a >> 4;  // shr
        Console.WriteLine(andResult);
        Console.WriteLine(notResult);
    }

    static void TestExceptionHandling()
    {
        // Try/catch (exercises try/catch IR, exception handler info)
        try
        {
            int x = 10;
            int y = 0;
            if (y == 0)
                throw new Exception("Division by zero");
            Console.WriteLine(x / y);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Caught exception");
        }

        // Try/finally
        try
        {
            Console.WriteLine("In try");
        }
        finally
        {
            Console.WriteLine("In finally");
        }
    }

    static void TestCustomExceptions()
    {
        // Test 1: throw and catch custom exception
        try
        {
            throw new CustomException("custom error", 42);
        }
        catch (CustomException ex)
        {
            Console.WriteLine("Caught CustomException: " + ex.ErrorCode.ToString());
        }

        // Test 2: catch derived custom exception as base custom exception
        try
        {
            throw new DerivedCustomException("derived error");
        }
        catch (CustomException ex)
        {
            Console.WriteLine("Caught DerivedCustomException as CustomException: " + ex.ErrorCode.ToString());
        }

        // Test 3: catch custom exception as base Exception
        try
        {
            throw new CustomException("base catch", 7);
        }
        catch (Exception)
        {
            Console.WriteLine("Caught CustomException as Exception");
        }
    }

    static void TestVirtualCalls()
    {
        Animal dog = new Dog("Rex");
        Animal cat = new Cat("Whiskers");
        Console.WriteLine(dog.Speak());  // callvirt
        Console.WriteLine(cat.Speak());  // callvirt
        Console.WriteLine(Animal.GetCount());
    }

    static void TestCasting()
    {
        Animal animal = new Dog("Buddy");

        // isinst
        Dog? maybeDog = animal as Dog;
        if (maybeDog != null)
            Console.WriteLine("Is a dog");

        // castclass
        Dog definitelyDog = (Dog)animal;
        Console.WriteLine(definitelyDog.Speak());

        // isinst returning null
        Cat? maybeCat = animal as Cat;
        if (maybeCat == null)
            Console.WriteLine("Not a cat");
    }

    static void TestBoxingUnboxing()
    {
        int value = 42;
        object boxed = value;           // box
        int unboxed = (int)boxed;       // unbox.any
        Console.WriteLine(unboxed);

        Color color = Color.Green;
        object boxedEnum = color;       // box enum
        Console.WriteLine(boxedEnum);
    }

    static void TestSwitchStatement()
    {
        int day = 3;
        switch (day)
        {
            case 1: Console.WriteLine("Mon"); break;
            case 2: Console.WriteLine("Tue"); break;
            case 3: Console.WriteLine("Wed"); break;
            case 4: Console.WriteLine("Thu"); break;
            default: Console.WriteLine("Other"); break;
        }
    }

    static void TestFloatDouble()
    {
        float f = 3.14f;          // ldc.r4
        double d = 2.71828;       // ldc.r8
        float sum = f + 1.0f;
        double product = d * 2.0;
        Console.WriteLine(sum);
        Console.WriteLine(product);
    }

    static void TestLong()
    {
        long big = 1234567890123L;   // ldc.i8
        long neg = -1L;
        long sum = big + neg;
        Console.WriteLine(sum);
    }

    static void TestStaticFields()
    {
        Console.WriteLine(_globalValue);   // ldsfld
        _globalValue = 200;                // stsfld
        Console.WriteLine(_globalValue);
    }

    static void TestNullAndDup()
    {
        object? obj = null;           // ldnull
        if (obj == null)
            Console.WriteLine("null");

        // Dup is used internally by C# compiler e.g., in compound operations
        int x = 5;
        Console.WriteLine(x);
    }

    static void TestStringOps(string input)
    {
        // String.Concat (mapped BCL)
        string greeting = string.Concat("Hello, ", input);
        Console.WriteLine(greeting);

        // String.IsNullOrEmpty
        if (!string.IsNullOrEmpty(input))
        {
            Console.WriteLine(input.Length);  // String.get_Length
        }
    }

    static void TestObjectMethods()
    {
        object obj = new Dog("test");
        string s = obj.ToString();            // Object.ToString
        int hash = obj.GetHashCode();         // Object.GetHashCode
        bool eq = obj.Equals(obj);            // Object.Equals
        Console.WriteLine(s);
        Console.WriteLine(hash);
    }

    static void TestMathOps()
    {
        double x = -3.5;
        Console.WriteLine(Math.Abs(x));       // Math.Abs
        Console.WriteLine(Math.Max(1.0, 2.0)); // Math.Max
        Console.WriteLine(Math.Min(1.0, 2.0)); // Math.Min
        Console.WriteLine(Math.Sqrt(4.0));     // Math.Sqrt
        Console.WriteLine(Math.Floor(3.7));    // Math.Floor
        Console.WriteLine(Math.Pow(2.0, 3.0)); // Math.Pow
    }

    static void TestStructOps()
    {
        // initobj: default struct initialization
        Point p = default;                    // initobj
        p.X = 10;                            // stfld on value type (via ldloca)
        p.Y = 20;
        Console.WriteLine(p.X);
        Console.WriteLine(p.Y);
    }

    static void TestMoreConversions()
    {
        long l = 1000L;
        uint u = (uint)l;                    // conv.u4
        ushort us = (ushort)l;               // conv.u2
        sbyte sb = (sbyte)l;                 // conv.i1
        ulong ul = (ulong)l;                // conv.u8
        Console.WriteLine(u);
        Console.WriteLine(us);

        // IntPtr / UIntPtr conversions (conv.i / conv.u)
        int x = 42;
        IntPtr ip = (IntPtr)x;
        UIntPtr uip = (UIntPtr)(uint)x;
        int back = (int)ip;
        Console.WriteLine(back);

        // conv.r.un — unsigned to float
        ulong bigUnsigned = 1000UL;
        double d = (double)bigUnsigned;      // conv.r.un
        Console.WriteLine(d);
    }

    static void ModifyArg(int x)
    {
        x = x + 1;                           // starg
        Console.WriteLine(x);
    }

    // Many params → triggers ldarg.s (> 3 params on instance method)
    static int ManyParams(int a, int b, int c, int d, int e, int f)
    {
        return a + b + c + d + e + f;
    }

    static void TestConsoleWrite()
    {
        Console.Write("No newline");       // Console.Write (not WriteLine)
        Console.WriteLine();
    }

    static void TestMoreMathOps()
    {
        Console.WriteLine(Math.Ceiling(3.2));  // Math.Ceiling
        Console.WriteLine(Math.Round(3.7));    // Math.Round
        Console.WriteLine(Math.Sin(1.0));      // Math.Sin
        Console.WriteLine(Math.Cos(1.0));      // Math.Cos
        Console.WriteLine(Math.Tan(0.5));      // Math.Tan
        Console.WriteLine(Math.Log(2.718));    // Math.Log
        Console.WriteLine(Math.Log10(100.0));  // Math.Log10
        Console.WriteLine(Math.Exp(1.0));      // Math.Exp
        Console.WriteLine(Math.Asin(0.5));     // Math.Asin
        Console.WriteLine(Math.Acos(0.5));     // Math.Acos
        Console.WriteLine(Math.Atan(1.0));     // Math.Atan
        Console.WriteLine(Math.Atan2(1.0, 1.0)); // Math.Atan2
    }

    static void TestConstants()
    {
        // Exercise ldc.i4.6, ldc.i4.7, ldc.i4.8
        int a = 6;
        int b = 7;
        int c = 8;
        Console.WriteLine(a + b + c);
    }

    static void TestReadonlyField()
    {
        var cfg = new Config("test");
        Console.WriteLine(cfg.MaxRetries);
        Console.WriteLine(cfg.Name);
    }

    static void TestInterfaceDispatch()
    {
        ISpeak speaker = new Duck("Donald");
        Console.WriteLine(speaker.GetSound());
    }

    static void TestFinalizer()
    {
        var resource = new Resource("test");
        Console.WriteLine(resource.GetName());
    }

    static void TestOperator()
    {
        var v1 = new Vector2(1.0f, 2.0f);
        var v2 = new Vector2(3.0f, 4.0f);
        var v3 = v1 + v2;
        Console.WriteLine(v3.X);
        Console.WriteLine(v3.Y);
    }

    static void TestProperties()
    {
        var person = new Person("Alice", 30);
        Console.WriteLine(person.Name);     // auto-property getter
        Console.WriteLine(person.Age);      // auto-property getter
        person.ManualProp = 5;              // manual property setter (value * 2)
        Console.WriteLine(person.ManualProp); // manual property getter → 10
    }

    static void TestForeachArray()
    {
        int[] numbers = new int[] { 10, 20, 30, 40, 50 };
        int sum = 0;
        foreach (int n in numbers)
        {
            sum += n;
        }
        Console.WriteLine(sum); // 150
    }

    static void TestUsing()
    {
        var res = new DisposableResource("test");
        Console.WriteLine("Using resource");
        res.Dispose();
        // Should print: "Using resource" then "Disposed: test"
    }

    static void TestUsingStatement()
    {
        // Real using statement with System.IDisposable
        using (var res = new ManagedResource("auto"))
        {
            Console.WriteLine("Inside using");
        }
        // Should print: "Inside using" then "Disposed managed: auto"
    }

    static void TestExceptionFilter()
    {
        // catch (Exception e) when (condition) — ECMA-335 filter handler
        int result = 0;
        try
        {
            throw new InvalidOperationException();
        }
        catch (Exception) when (result == 0)
        {
            result = 1;
        }
        Console.WriteLine(result); // 1

        // Filter that rejects — exception should propagate
        try
        {
            try
            {
                throw new InvalidOperationException();
            }
            catch (Exception) when (false)
            {
                result = -1; // Should not reach here
            }
        }
        catch (Exception)
        {
            result = 2; // Caught by outer handler
        }
        Console.WriteLine(result); // 2
    }

    static int StaticAdd(int a, int b)
    {
        return a + b;
    }

    static void TestDelegate()
    {
        // Static method delegate
        MathOp add = StaticAdd;
        Console.WriteLine(add(3, 4)); // 7
    }

    static void TestGenerics()
    {
        var intW = new Wrapper<int>(42);
        Console.WriteLine(intW.GetValue()); // 42
        intW.SetValue(100);
        Console.WriteLine(intW.GetValue()); // 100

        var strW = new Wrapper<string>("Hello");
        Console.WriteLine(strW.GetValue()); // Hello
    }

    // Exercises 'rethrow' IL instruction
    static void TestRethrow()
    {
        try
        {
            try
            {
                throw new Exception("inner");
            }
            catch
            {
                throw; // rethrow instruction
            }
        }
        catch (Exception e)
        {
            Console.WriteLine("Rethrown");
        }
    }

    // Exercises float/double NaN and Infinity constants (ldc.r4/ldc.r8 special values)
    static void TestSpecialFloats()
    {
        float fnan = float.NaN;
        float fposInf = float.PositiveInfinity;
        float fnegInf = float.NegativeInfinity;
        double dnan = double.NaN;
        double dposInf = double.PositiveInfinity;
        double dnegInf = double.NegativeInfinity;
        Console.WriteLine(fnan);
        Console.WriteLine(fposInf);
        Console.WriteLine(dnan);
        Console.WriteLine(dnegInf);
    }

    // Exercises Delegate.Combine and Delegate.Remove BCL mapping
    static void TestDelegateCombine()
    {
        MathOp d1 = StaticAdd;
        MathOp d2 = StaticAdd;
        MathOp combined = d1 + d2;    // Delegate.Combine
        MathOp removed = combined - d2; // Delegate.Remove
        Console.WriteLine(removed(1, 2)); // 3
    }

    // Exercises Math.Abs overloads (float, double, int)
    static void TestMathAbsOverloads()
    {
        float f = -3.14f;
        double d = -2.718;
        int i = -42;
        Console.WriteLine(Math.Abs(f));  // fabsf
        Console.WriteLine(Math.Abs(d));  // fabs
        Console.WriteLine(Math.Abs(i));  // abs
    }

    // Exercises AllFieldTypes with Int16/Char/Int64/Double fields
    static void TestAllFieldTypes()
    {
        var obj = new AllFieldTypes();
        obj.ShortField = 10;
        obj.CharField = 'A';
        obj.LongField = 123456789L;
        obj.DoubleField = 3.14;
        obj.ByteField = 255;
        obj.FloatField = 1.5f;
        Console.WriteLine(obj.ShortField);
        Console.WriteLine(obj.LongField);
    }

    // Exercises virtual dispatch on System.Object methods (ToString/GetHashCode/Equals)
    // These go through the System.Object vtable fallback path when called virtually
    static void TestVirtualObjectDispatch()
    {
        // Using a local Dog typed as object forces callvirt on System.Object
        object obj = new Dog("Sparky");
        // These are callvirt System.Object::ToString(), etc.
        string s = obj.ToString();
        int h = obj.GetHashCode();
        bool eq = obj.Equals(obj);
        Console.WriteLine(s);
        Console.WriteLine(h);
    }

    // Exercises ldelem/stelem for all element type variants
    static void TestTypedArrays()
    {
        // Create arrays of different types
        byte[] bytes = new byte[2];
        short[] shorts = new short[2];
        long[] longs = new long[2];
        float[] floats = new float[2];
        double[] doubles = new double[2];
        object[] objects = new object[2];

        // Store elements (exercises stelem.i1, stelem.i2, stelem.i8, stelem.r4, stelem.r8, stelem.ref)
        bytes[0] = 42;
        shorts[0] = 1000;
        longs[0] = 123456789L;
        floats[0] = 3.14f;
        doubles[0] = 2.718;
        objects[0] = "hello";

        // Load elements (exercises ldelem.u1, ldelem.i2, ldelem.i8, ldelem.r4, ldelem.r8, ldelem.ref)
        byte b = bytes[0];
        short s = shorts[0];
        long l = longs[0];
        float f = floats[0];
        double d = doubles[0];
        object o = objects[0];

        Console.WriteLine(b);
        Console.WriteLine(s);
        Console.WriteLine(l);
        Console.WriteLine(f);
        Console.WriteLine(d);
        Console.WriteLine(o);
    }

    // Exercises GenericHelper<T> with array operations and ref params in generic context
    static void TestGenericHelper()
    {
        var intHelper = new GenericHelper<int>(3);
        intHelper.Set(0, 10);
        intHelper.Set(1, 20);
        intHelper.Set(2, 30);
        Console.WriteLine(intHelper.Get(0)); // 10
        Console.WriteLine(intHelper.Count()); // 3

        // Test Swap with ref parameters (exercises ByReferenceType in generic resolution)
        int x = 100;
        int y = 200;
        intHelper.Swap(ref x, ref y);
        Console.WriteLine(x); // 200
        Console.WriteLine(y); // 100

        var strHelper = new GenericHelper<string>(2);
        strHelper.Set(0, "hello");
        strHelper.Set(1, "world");
        Console.WriteLine(strHelper.Get(0)); // hello

        string s1 = "first";
        string s2 = "second";
        strHelper.Swap(ref s1, ref s2);
        Console.WriteLine(s1); // second
    }

    // Exercises stateless lambda (generates <>c static display class with cached delegate)
    static void TestLambda()
    {
        // Stateless lambda → C# compiler generates <>c singleton class + cached delegate
        VoidAction greet = () => Console.WriteLine("Lambda!");
        greet();

        // Lambda with parameter
        IntFunc doubler = (int x) => x * 2;
        Console.WriteLine(doubler(21)); // 42

        // Lambda passed as argument
        int result = ApplyFunc(doubler, 10);
        Console.WriteLine(result); // 20
    }

    static int ApplyFunc(IntFunc f, int arg)
    {
        return f(arg);
    }

    // Exercises closure / variable capture (generates <>c__DisplayClass with captured fields)
    static void TestClosure()
    {
        int captured = 100;
        IntFunc addCaptured = (int x) => x + captured;
        Console.WriteLine(addCaptured(42)); // 142

        // Multiple captures
        int a = 10;
        int b = 20;
        VoidAction printSum = () => Console.WriteLine(a + b);
        printSum(); // 30
    }

    // Exercises events (add_/remove_ methods + delegate backing field + invoke)
    static void TestEvents()
    {
        var source = new EventSource();

        // Subscribe with method reference
        NotifyDelegate handler = OnNotified;
        source.Subscribe(handler);

        // Fire event
        source.Fire(42); // prints "Notified: 42"

        // Subscribe second handler
        NotifyDelegate handler2 = OnNotified2;
        source.Subscribe(handler2);
        source.Fire(7); // prints "Notified: 7" then "Also notified: 7"

        // Unsubscribe first handler
        source.Unsubscribe(handler);
        source.Fire(99); // prints only "Also notified: 99"
    }

    // Exercises generic METHOD instantiations (GenericInstanceMethod in Cecil)
    static void TestGenericMethods()
    {
        // Identity<int> and Identity<string> — two instantiations of same generic method
        int x = GenericUtils.Identity<int>(42);
        Console.WriteLine(x); // 42

        string s = GenericUtils.Identity<string>("hello");
        Console.WriteLine(s); // hello

        // Swap<int>
        int a = 10;
        int b = 20;
        GenericUtils.Swap<int>(ref a, ref b);
        Console.WriteLine(a); // 20
        Console.WriteLine(b); // 10
    }

    static void OnNotified(int value)
    {
        Console.WriteLine(string.Concat("Notified: ", value.ToString()));
    }

    static void OnNotified2(int value)
    {
        Console.WriteLine(string.Concat("Also notified: ", value.ToString()));
    }

    // Non-generic ref int swap — exercises Ldind_I4, Stind_I4
    static void SwapInt(ref int a, ref int b)
    {
        int temp = a;
        a = b;
        b = temp;
    }

    // Non-generic ref object swap — exercises Ldind_Ref, Stind_Ref
    static void SwapObj(ref object a, ref object b)
    {
        object temp = a;
        a = b;
        b = temp;
    }

    // Exercises Ldind_I4, Stind_I4, Ldind_Ref, Stind_Ref via ref parameters
    static void TestRefParams()
    {
        int x = 10;
        int y = 20;
        SwapInt(ref x, ref y);
        Console.WriteLine(x); // 20
        Console.WriteLine(y); // 10

        object a = "first";
        object b = "second";
        SwapObj(ref a, ref b);
        Console.WriteLine(a); // second
        Console.WriteLine(b); // first
    }

    // Exercises Ldsflda (load address of static field for ref parameter)
    static void TestStaticFieldRef()
    {
        int local = 50;
        SwapInt(ref _globalValue, ref local);
        Console.WriteLine(_globalValue); // 50
        Console.WriteLine(local); // previous value of _globalValue
    }

    // Exercises Ldvirtftn (create delegate from virtual method)
    static void TestVirtualDelegate()
    {
        Animal a = new Dog("Rex");
        StringFunc speak = a.Speak; // generates ldvirtftn for virtual method
        Console.WriteLine(speak()); // "Woof!"
    }

    // Exercises generic value type (RegisterValueType for generic instances)
    static void TestGenericStruct()
    {
        var p = new Pair<int>(10, 20);
        Console.WriteLine(p.First);  // 10
        Console.WriteLine(p.Second); // 20
    }

    // Exercises indexer (get_Item / set_Item are just ordinary method calls in IL)
    static void TestIndexer()
    {
        var list = new IntList(3);
        list[0] = 10;    // set_Item
        list[1] = 20;
        list[2] = 30;
        Console.WriteLine(list[0]); // get_Item → 10
        Console.WriteLine(list[1]); // 20
        Console.WriteLine(list[2]); // 30
    }

    // Exercises default parameters (C# compiler fills defaults at call site — no IL semantics)
    static void TestDefaultParameters()
    {
        int r1 = DefaultParamHelper.Add(5);        // b defaults to 10 → 15
        int r2 = DefaultParamHelper.Add(5, 20);    // explicit → 25
        Console.WriteLine(r1); // 15
        Console.WriteLine(r2); // 25

        string g1 = DefaultParamHelper.Greet("World");             // "Hello, World"
        string g2 = DefaultParamHelper.Greet("World", "Goodbye");  // "Goodbye, World"
        Console.WriteLine(g1);
        Console.WriteLine(g2);
    }

    // Exercises init-only setter (modreq(IsExternalInit) — CIL2CPP ignores modreq)
    static void TestInitOnlySetter()
    {
        var point = new ImmutablePoint { X = 42, Y = 99 };
        Console.WriteLine(point.X); // 42
        Console.WriteLine(point.Y); // 99
    }

    // Helper methods that force checked arithmetic in IL
    static int CheckedAdd(int a, int b) { return checked(a + b); }
    static int CheckedSub(int a, int b) { return checked(a - b); }
    static int CheckedMul(int a, int b) { return checked(a * b); }

    // Helper methods that force checked conversions in IL (conv.ovf.*)
    static byte CheckedToByte(int x) { return checked((byte)x); }
    static sbyte CheckedToSByte(int x) { return checked((sbyte)x); }
    static short CheckedToShort(long x) { return checked((short)x); }
    static uint CheckedToUInt(int x) { return checked((uint)x); }

    // Exercises checked arithmetic (Add_Ovf, Sub_Ovf, Mul_Ovf IL opcodes)
    static void TestCheckedOverflow()
    {
        Console.WriteLine(CheckedAdd(100, 200)); // 300
        Console.WriteLine(CheckedSub(500, 200)); // 300
        Console.WriteLine(CheckedMul(15, 20));   // 300

        // Overflow that gets caught
        try
        {
            int x = int.MaxValue;
            int y = CheckedAdd(x, 1); // OverflowException
            Console.WriteLine(y);
        }
        catch (OverflowException)
        {
            Console.WriteLine("Overflow caught");
        }

        // Checked conversions — valid
        Console.WriteLine(CheckedToByte(200));    // 200
        Console.WriteLine(CheckedToSByte(100));   // 100
        Console.WriteLine(CheckedToShort(30000L));// 30000
        Console.WriteLine(CheckedToUInt(42));     // 42

        // Checked conversion — overflow
        try
        {
            byte b = CheckedToByte(300); // OverflowException: 300 > 255
            Console.WriteLine(b);
        }
        catch (OverflowException)
        {
            Console.WriteLine("Conv overflow caught");
        }

        // Checked conversion — negative to unsigned
        try
        {
            uint u = CheckedToUInt(-1); // OverflowException: -1 < 0
            Console.WriteLine(u);
        }
        catch (OverflowException)
        {
            Console.WriteLine("Conv negative overflow caught");
        }
    }

    // Exercises Nullable<T> (System.Nullable`1 — BCL generic struct)
    static void TestNullable()
    {
        int? a = 42;
        Console.WriteLine(a.HasValue);           // True
        Console.WriteLine(a.Value);              // 42
        Console.WriteLine(a.GetValueOrDefault()); // 42

        int? b = null;
        Console.WriteLine(b.HasValue);            // False
        Console.WriteLine(b.GetValueOrDefault(99)); // 99

        // Test nullable with value access
        int? c = 10;
        if (c.HasValue)
            Console.WriteLine(c.Value);           // 10
    }

    // Exercises Nullable<T> boxing (ECMA-335 III.4.1: box Nullable<T> unwraps)
    static void TestNullableBoxing()
    {
        // Boxing null Nullable<T> should produce null reference
        int? nullVal = null;
        object boxedNull = nullVal;
        Console.WriteLine(boxedNull == null);  // True

        // Boxing non-null Nullable<T> should box the inner T value
        int? hasVal = 42;
        object boxedVal = hasVal;
        Console.WriteLine(boxedVal != null);   // True

        // Unbox back to int (not Nullable<int>) should work
        int unboxed = (int)boxedVal;
        Console.WriteLine(unboxed);            // 42
    }

    // Exercises ValueTuple (tuple literal syntax → System.ValueTuple`N)
    static void TestValueTuple()
    {
        var t = (1, 2);
        Console.WriteLine(t.Item1);  // 1
        Console.WriteLine(t.Item2);  // 2

        (int x, int y) = (10, 20);
        Console.WriteLine(x);  // 10
        Console.WriteLine(y);  // 20
    }

    // Exercises ValueTuple.Equals and GetHashCode (proper field comparison)
    static void TestValueTupleEquals()
    {
        var a = (1, 2);
        var b = (1, 2);
        var c = (3, 4);

        // Equals via boxing (ValueTuple<T1,T2>.Equals(object))
        Console.WriteLine(a.Equals(b));  // True
        Console.WriteLine(a.Equals(c));  // False

        // GetHashCode: equal tuples should have equal hashes
        Console.WriteLine(a.GetHashCode() == b.GetHashCode());  // True
    }

    // Exercises unbox.any on reference types (ECMA-335 III.4.33: castclass semantics)
    static void TestUnboxAnyRefType()
    {
        object obj = "hello";
        string s = (string)obj;          // unbox.any with reference type → castclass
        Console.WriteLine(s);            // hello

        object boxedInt = 42;
        int val = (int)boxedInt;          // unbox.any with value type → unbox
        Console.WriteLine(val);           // 42
    }

    // Exercises abstract class with 3-level inheritance chain (vtable dispatch)
    static void TestAbstractInheritance()
    {
        Shape s = new UnitCircle();
        Console.WriteLine(s.Area());     // 3.14159 (inherited from Circle)
        Console.WriteLine(s.Describe()); // Circle  (inherited from Circle)

        Shape c = new Circle(2.0);
        Console.WriteLine(c.Area());     // 12.56636 (Circle.Area)
    }

    // Exercises implicit/explicit conversion operators (op_Implicit, op_Explicit)
    static void TestConversionOperators()
    {
        Celsius temp = new Celsius(100.0);
        double d = temp;                    // implicit operator double
        Console.WriteLine(d);               // 100

        Celsius back = (Celsius)36.6;       // explicit operator Celsius
        Console.WriteLine(back.Value);      // 36.6
    }

    // Exercises static method triggering cctor (ECMA-335 II.10.5.3.1)
    static void TestStaticMethodCctor()
    {
        // LazyInit.GetValue() is a static method call — must trigger cctor first
        int val = LazyInit.GetValue();
        Console.WriteLine(val);             // 42
    }

    // Exercises Object.ReferenceEquals (static method on System.Object)
    static void TestReferenceEquals()
    {
        var a = new Dog("Rex");
        var b = a;
        var c = new Dog("Rex");
        Console.WriteLine(Object.ReferenceEquals(a, b));  // True  (same reference)
        Console.WriteLine(Object.ReferenceEquals(a, c));  // False (different objects)
        Console.WriteLine(Object.ReferenceEquals(null, null));  // True
    }

    // Exercises Object.MemberwiseClone (protected method on System.Object)
    static void TestMemberwiseClone()
    {
        var orig = new Cloneable(42);
        var clone = orig.ShallowCopy();
        Console.WriteLine(clone.Value);                         // 42
        Console.WriteLine(Object.ReferenceEquals(orig, clone)); // False
    }

    // Exercises overloaded virtual methods with same name, different param types
    static void TestOverloadedVirtual()
    {
        Formatter f = new PrefixFormatter();
        Console.WriteLine(f.Format(42));       // 42 (calls Format(int) override)
        Console.WriteLine(f.Format("hello"));  // hello (calls Format(string) override)
    }

    // Exercises interface inheritance (IB : IA): dispatch through base interface
    static void TestInterfaceInheritance()
    {
        IDerived d = new InterfaceInheritImpl();
        Console.WriteLine(d.BaseMethod());    // "base" — dispatched via IBase vtable
        Console.WriteLine(d.DerivedMethod()); // "derived" — dispatched via IDerived vtable

        // Also call through IBase reference
        IBase b = d;
        Console.WriteLine(b.BaseMethod());    // "base"
    }

    // Exercises overloaded interface methods: same name, different param types
    static void TestOverloadedInterface()
    {
        var proc = new Processor();
        IProcess ip = proc;
        ip.Process(42);
        ip.Process("hello");
        Console.WriteLine(proc.LastInt);     // 42
        Console.WriteLine(proc.LastString);  // hello
    }

    // Exercises multi-parameter generic type (KeyValue<K,V>)
    static void TestMultiParamGeneric()
    {
        var kv = new KeyValue<int, string>(42, "hello");
        Console.WriteLine(kv.Key);    // 42
        Console.WriteLine(kv.Value);  // hello
    }

    // Exercises generic type inheriting from another generic type
    static void TestGenericInheritance()
    {
        var sw = new SpecialWrapper<int>(42, "tagged");
        Console.WriteLine(sw.GetValue());  // 42 (inherited from Wrapper<int>)
        Console.WriteLine(sw.Tag);         // tagged (own field)
    }

    // Exercises nested generic instantiation (Wrapper<Wrapper<int>>)
    static void TestNestedGeneric()
    {
        var inner = new Wrapper<int>(99);
        var outer = new Wrapper<Wrapper<int>>(inner);
        Console.WriteLine(outer.GetValue().GetValue());  // 99
    }

    // Exercises generic type with static constructor (cctor per specialization)
    static void TestGenericCctor()
    {
        var intCache = new GenericCache<int>(42);
        Console.WriteLine(intCache.Item);  // 42
        Console.WriteLine(GenericCache<int>.GetInitCount());  // 1
    }

    // Exercises record types (compiler-generated ToString, Equals, <Clone>$)
    static void TestRecord()
    {
        var p1 = new PersonRecord("Alice", 30);
        Console.WriteLine(p1.Name);     // Alice
        Console.WriteLine(p1.Age);      // 30

        var p2 = new PersonRecord("Alice", 30);
        Console.WriteLine(p1 == p2);    // True
        Console.WriteLine(p1 != p2);    // False

        var p3 = p1 with { Age = 31 };
        Console.WriteLine(p3.Age);      // 31
        Console.WriteLine(p1 == p3);    // False
    }

    // Exercises record struct (value type record — no Clone, uses value copy)
    static void TestRecordStruct()
    {
        var p1 = new PointRecord(10, 20);
        Console.WriteLine(p1.X);        // 10
        Console.WriteLine(p1.Y);        // 20

        var p2 = new PointRecord(10, 20);
        Console.WriteLine(p1 == p2);    // True
        Console.WriteLine(p1 != p2);    // False

        var p3 = new PointRecord(30, 40);
        Console.WriteLine(p1 == p3);    // False
    }

    // Async helper: simple async method returning Task<int>
    static async Task<int> ComputeAsync(int x)
    {
        await Task.CompletedTask;
        return x * 2;
    }

    // Exercises explicit interface implementation (.override directive)
    static void TestExplicitInterface()
    {
        var logger = new FileLogger();

        // Call through interface — explicit impl
        ILogger ilog = logger;
        ilog.Log("test");
        Console.WriteLine(logger.LogCount);  // 1

        // Call through interface — implicit impl
        IFormatter fmt = logger;
        Console.WriteLine(fmt.Format(42));   // 42
    }

    // Exercises method hiding (new virtual / newslot)
    static void TestMethodHiding()
    {
        BaseDisplay b = new BaseDisplay();
        Console.WriteLine(b.Show());      // BaseDisplay
        Console.WriteLine(b.Value());     // 1

        DerivedDisplay d = new DerivedDisplay();
        Console.WriteLine(d.Show());      // DerivedDisplay
        Console.WriteLine(d.Value());     // 2

        // Through BaseDisplay reference: Show() should dispatch to BaseDisplay's slot
        // because DerivedDisplay.Show is 'new virtual' (not override)
        BaseDisplay bd = d;
        Console.WriteLine(bd.Show());     // BaseDisplay  (method hiding!)
        Console.WriteLine(bd.Value());    // 2  (normal override)

        // FinalDisplay overrides DerivedDisplay.Show
        FinalDisplay f = new FinalDisplay();
        DerivedDisplay df = f;
        Console.WriteLine(df.Show());     // FinalDisplay  (override of hidden slot)
    }

    // Exercises sizeof opcode (only user-defined structs emit sizeof IL; Roslyn constants for builtins)
    static unsafe void TestSizeOf()
    {
        Console.WriteLine(sizeof(TinyStruct));  // 1
        Console.WriteLine(sizeof(BigStruct));   // 24
    }

    // Exercises while loop (backward branch)
    static void TestWhileLoop()
    {
        int i = 0;
        while (i < 5) { i++; }
        Console.WriteLine(i); // 5
    }

    // Exercises do-while loop (backward conditional branch)
    static void TestDoWhileLoop()
    {
        int count = 0;
        do { count++; } while (count < 3);
        Console.WriteLine(count); // 3
    }

    // Exercises for loop (forward + backward branches)
    static void TestForLoop()
    {
        int sum = 0;
        for (int i = 0; i < 10; i++) { sum += i; }
        Console.WriteLine(sum); // 45
    }

    // Exercises nested loops with break/continue
    static void TestNestedLoopBreakContinue()
    {
        int count = 0;
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (j == 1) continue;
                if (i == 2) break;
                count++;
            }
        }
        Console.WriteLine(count); // 4 (i=0:j=0,j=2; i=1:j=0,j=2)
    }

    // Exercises goto (explicit forward + backward unconditional branch)
    static void TestGoto()
    {
        int n = 0;
        start:
        n++;
        if (n < 3) goto start;
        Console.WriteLine(n); // 3

        goto skip;
        Console.WriteLine("skipped");
        skip:
        Console.WriteLine("reached"); // reached
    }

    // Exercises nested if/else (multiple branches)
    static void TestNestedIfElse()
    {
        int x = 5;
        if (x > 10)
            Console.WriteLine("big");
        else if (x > 3)
            Console.WriteLine("medium"); // medium
        else
            Console.WriteLine("small");
    }

    // Exercises ternary operator (conditional branch pattern)
    static void TestTernary()
    {
        int x = 5;
        string result = x > 3 ? "big" : "small";
        Console.WriteLine(result); // big
    }

    // Exercises short-circuit evaluation (&& / ||)
    static void TestShortCircuit()
    {
        int sideEffect = 0;
        bool r1 = false && (++sideEffect > 0);
        Console.WriteLine(sideEffect); // 0 (short-circuited, ++ not executed)

        bool r2 = true || (++sideEffect > 0);
        Console.WriteLine(sideEffect); // 0 (short-circuited, ++ not executed)

        bool r3 = true && (++sideEffect > 0);
        Console.WriteLine(sideEffect); // 1 (not short-circuited, ++ executed)
    }

    // Exercises unsigned comparison (bge.un, bgt.un, ble.un, blt.un opcodes)
    static void TestUnsignedComparison()
    {
        uint a = 10;
        uint b = 20;
        Console.WriteLine(a < b);   // True  (blt.un)
        Console.WriteLine(a > b);   // False (bgt.un)
        Console.WriteLine(a <= b);  // True  (ble.un)
        Console.WriteLine(a >= b);  // False (bge.un)

        // Char comparisons are also unsigned
        char c1 = 'A';
        char c2 = 'Z';
        Console.WriteLine(c1 < c2); // True
    }

    // Exercises float/double NaN comparison behavior
    static void TestFloatNaNComparison()
    {
        double nan = double.NaN;
        Console.WriteLine(nan == nan);   // False
        Console.WriteLine(nan != nan);   // True
        Console.WriteLine(nan > 0);      // False
        Console.WriteLine(nan < 0);      // False
    }

    // Exercises async/await (state machine + Task<T> + AsyncTaskMethodBuilder)
    static void TestAsync()
    {
        var task = ComputeAsync(21);
        Console.WriteLine(task.Result);  // 42
    }

    // Exercises arr[^1] — Roslyn inlines to arr[arr.Length - 1] (no System.Index)
    // Also exercises explicit Index constructor and GetOffset
    static void TestIndexFromEnd()
    {
        int[] arr = new int[] { 10, 20, 30, 40, 50 };

        // arr[^1] — Roslyn optimizes: arr[arr.Length - 1]
        int last = arr[^1];
        Console.WriteLine(last);  // 50

        // arr[^2]
        int secondLast = arr[^2];
        Console.WriteLine(secondLast);  // 40

        // Explicit Index construction + GetOffset
        int val = IndexRangeHelper.GetFromEnd(arr, 3);
        Console.WriteLine(val);  // 30 (arr[2])
    }

    // Exercises arr[1..3] — uses RuntimeHelpers.GetSubArray<T>
    static void TestRangeSlice()
    {
        int[] arr = new int[] { 10, 20, 30, 40, 50 };

        // arr[1..3] — slice from index 1 to 3 (exclusive)
        int[] slice = arr[1..3];
        Console.WriteLine(slice.Length);  // 2
        Console.WriteLine(slice[0]);      // 20
        Console.WriteLine(slice[1]);      // 30

        // arr[..2] — from start to 2
        int[] first2 = arr[..2];
        Console.WriteLine(first2.Length);  // 2
        Console.WriteLine(first2[0]);      // 10

        // arr[3..] — from 3 to end
        int[] last2 = arr[3..];
        Console.WriteLine(last2.Length);  // 2
        Console.WriteLine(last2[0]);      // 40
    }

    // Exercises string[1..4] — Roslyn compiles to String.Substring(start, length)
    static void TestStringSlice()
    {
        string s = "hello world";

        // s[1..4] → s.Substring(1, 3) = "ell"
        string sub = s[1..4];
        Console.WriteLine(sub);  // ell

        // s[6..] → s.Substring(6) or s.Substring(6, 5) = "world"
        string end = s[6..];
        Console.WriteLine(end);  // world
    }

    // Exercises Index.Value and Index.IsFromEnd properties
    static void TestIndexProperties()
    {
        Index fromStart = new Index(3, false);
        Console.WriteLine(fromStart.Value);      // 3
        Console.WriteLine(fromStart.IsFromEnd);  // False

        Index fromEnd = new Index(2, true);
        Console.WriteLine(fromEnd.Value);        // 2
        Console.WriteLine(fromEnd.IsFromEnd);    // True

        // GetOffset with known length
        int offset = fromEnd.GetOffset(10);
        Console.WriteLine(offset);  // 8 (10 - 2)
    }

    // Exercises Range.GetOffsetAndLength (returns ValueTuple<int,int>)
    static void TestRangeGetOffsetAndLength()
    {
        // 1..^1 on length 5 → (1, 3)
        Range r = 1..^1;
        var (startOffset, length) = r.GetOffsetAndLength(5);
        Console.WriteLine(startOffset);  // 1
        Console.WriteLine(length);       // 3

        // Helper method that uses Range parameter
        int len = IndexRangeHelper.SliceLength(10, 2..8);
        Console.WriteLine(len);  // 6
    }

    // ===== Threading Tests =====

    // Exercises lock statement (Monitor.Enter/Exit + try-finally)
    static void TestLock()
    {
        object lockObj = new object();
        int counter = 0;
        lock (lockObj) { counter++; }
        Console.WriteLine(counter);  // 1
    }

    // Exercises Thread creation, Start, Join
    static void TestThread()
    {
        int result = 0;
        Thread t = new Thread(() => { result = 42; });
        t.Start();
        t.Join();
        Console.WriteLine(result);  // 42
    }

    // Exercises atomic operations
    static void TestInterlocked()
    {
        int val = 0;
        Interlocked.Increment(ref val);
        Console.WriteLine(val);  // 1
        int old = Interlocked.CompareExchange(ref val, 100, 1);
        Console.WriteLine(old);  // 1
        Console.WriteLine(val);  // 100
    }

    // Exercises Thread.Sleep
    static void TestThreadSleep()
    {
        Thread.Sleep(1);
        Console.WriteLine("Slept");
    }

    // Exercises Monitor.Wait/Pulse
    static void TestMonitorWaitPulse()
    {
        object sync = new object();
        bool signaled = false;
        Thread t = new Thread(() =>
        {
            lock (sync)
            {
                signaled = true;
                Monitor.Pulse(sync);
            }
        });
        lock (sync)
        {
            t.Start();
            while (!signaled)
                Monitor.Wait(sync);
        }
        t.Join();
        Console.WriteLine(signaled);  // True
    }

    // Exercises 64-bit atomics
    static void TestInterlockedLong()
    {
        long val = 0;
        Interlocked.Increment(ref val);
        Console.WriteLine(val);  // 1
    }

    // ===== Reflection Tests =====

    // Exercises typeof(T) → ldtoken + GetTypeFromHandle → Type properties
    static void TestTypeof()
    {
        Type t = typeof(int);
        Console.WriteLine(t.Name);       // Int32
        Console.WriteLine(t.FullName);   // System.Int32
        Console.WriteLine(t.IsValueType); // True
        Console.WriteLine(t.IsPrimitive); // True
        Console.WriteLine(t.IsClass);     // False
    }

    // Exercises obj.GetType() → object_get_type_managed → Type
    static void TestGetType()
    {
        object obj = new Dog("test");
        Type t = obj.GetType();
        Console.WriteLine(t.Name);       // Dog
    }

    // Exercises Type equality (op_Equality, op_Inequality) via cached Type objects
    static void TestTypeEquality()
    {
        Type t1 = typeof(int);
        Type t2 = typeof(int);
        Console.WriteLine(t1 == t2);    // True  (same cached Type object)
        Type t3 = typeof(string);
        Console.WriteLine(t1 == t3);    // False (different types)
        Console.WriteLine(t1 != t3);    // True
    }

    // Exercises Type.BaseType, IsAbstract, IsSealed, IsInterface
    static void TestTypeHierarchy()
    {
        Type t = typeof(Dog);
        Console.WriteLine(t.BaseType != null); // True (Animal)
        Console.WriteLine(t.IsAbstract);       // False
        Console.WriteLine(t.IsSealed);         // False
        Console.WriteLine(t.IsInterface);      // False
    }

    // Exercises Type.ToString() → returns FullName
    static void TestTypeToString()
    {
        Type t = typeof(Dog);
        Console.WriteLine(t.ToString()); // Dog
    }

    // Exercises boxing + GetType on value type
    static void TestGetTypeOnValueType()
    {
        int x = 42;
        Type t = x.GetType();
        Console.WriteLine(t.Name);       // Int32
        Console.WriteLine(t.IsValueType); // True
    }

    // ===== Collection tests (List<T>, Dictionary<K,V>) =====

    static void TestListInt()
    {
        var list = new List<int>();
        list.Add(10);
        list.Add(20);
        list.Add(30);
        Console.WriteLine(list.Count);   // 3
        Console.WriteLine(list[0]);      // 10
        Console.WriteLine(list[1]);      // 20
        list[1] = 25;
        Console.WriteLine(list[1]);      // 25
        list.RemoveAt(0);
        Console.WriteLine(list.Count);   // 2
        Console.WriteLine(list[0]);      // 25
        list.Insert(0, 5);
        Console.WriteLine(list[0]);      // 5
        Console.WriteLine(list.Contains(25)); // True
        Console.WriteLine(list.IndexOf(30));  // 2
        list.Clear();
        Console.WriteLine(list.Count);   // 0
    }

    static void TestListString()
    {
        var list = new List<string>();
        list.Add("hello");
        list.Add("world");
        Console.WriteLine(list.Count);   // 2
        Console.WriteLine(list[0]);      // hello
        Console.WriteLine(list[1]);      // world
        list.Remove("hello");
        Console.WriteLine(list.Count);   // 1
        Console.WriteLine(list[0]);      // world
    }

    static void TestDictionaryStringInt()
    {
        var dict = new Dictionary<string, int>();
        dict["one"] = 1;
        dict["two"] = 2;
        dict["three"] = 3;
        Console.WriteLine(dict.Count);             // 3
        Console.WriteLine(dict["two"]);            // 2
        Console.WriteLine(dict.ContainsKey("one"));  // True
        Console.WriteLine(dict.ContainsKey("four")); // False
        dict.Remove("two");
        Console.WriteLine(dict.Count);             // 2
        int val;
        if (dict.TryGetValue("three", out val))
            Console.WriteLine(val);                // 3
        dict.Clear();
        Console.WriteLine(dict.Count);             // 0
    }

    // ===== Async Concurrency Tests =====

    static async Task<int> DelayAndReturn(int value)
    {
        await Task.Delay(50);
        return value;
    }

    static void TestAsyncConcurrency()
    {
        // Test Task.Delay — real async delay
        var delayTask = DelayAndReturn(99);
        Console.WriteLine(delayTask.Result);  // 99

        // Test Task.FromResult
        var fromResult = Task.FromResult(42);
        Console.WriteLine(fromResult.Result);  // 42

        // Test basic async/await with delay
        var computeTask = ComputeAsync(10);
        Console.WriteLine(computeTask.Result); // 20
    }

    // ===== CancellationToken / TaskCompletionSource =====

    public static bool TestCancellationTokenDefault()
    {
        var token = CancellationToken.None;
        return !token.IsCancellationRequested && !token.CanBeCanceled;
    }

    public static bool TestCancellationTokenSourceCreate()
    {
        var cts = new CancellationTokenSource();
        var token = cts.Token;
        bool notCanceled = !token.IsCancellationRequested;
        cts.Cancel();
        bool canceled = token.IsCancellationRequested;
        return notCanceled && canceled;
    }

    public static bool TestCancellationTokenSourceDispose()
    {
        var cts = new CancellationTokenSource();
        cts.Dispose();
        return true; // no exception = success
    }

    public static async Task<int> TestTaskCompletionSourceAsync()
    {
        var tcs = new TaskCompletionSource<int>();
        tcs.SetResult(42);
        return await tcs.Task;
    }

    // ===== LINQ Extension Methods =====

    public static int LinqCount()
    {
        int[] nums = { 1, 2, 3, 4, 5 };
        return nums.Count(); // 5
    }

    public static int LinqCountPredicate()
    {
        int[] nums = { 1, 2, 3, 4, 5 };
        return nums.Count(x => x > 3); // 2
    }

    public static bool LinqAny()
    {
        int[] nums = { 1, 2, 3 };
        return nums.Any(); // true
    }

    public static bool LinqAnyPredicate()
    {
        int[] nums = { 1, 2, 3, 4, 5 };
        return nums.Any(x => x > 4); // true
    }

    public static bool LinqAll()
    {
        int[] nums = { 2, 4, 6, 8 };
        return nums.All(x => x % 2 == 0); // true
    }

    public static int LinqFirst()
    {
        int[] nums = { 10, 20, 30 };
        return nums.First(); // 10
    }

    public static int LinqFirstOrDefault()
    {
        int[] nums = {};
        return nums.FirstOrDefault(); // 0
    }

    public static int LinqSum()
    {
        int[] nums = { 1, 2, 3, 4, 5 };
        return nums.Sum(); // 15
    }

    public static int LinqMin()
    {
        int[] nums = { 3, 1, 4, 1, 5 };
        return nums.Min(); // 1
    }

    public static int LinqMax()
    {
        int[] nums = { 3, 1, 4, 1, 5 };
        return nums.Max(); // 5
    }

    public static bool LinqContains()
    {
        int[] nums = { 1, 2, 3, 4, 5 };
        return nums.Contains(3); // true
    }

    // ── String operations ─────────────────────────────────

    public static string StringFormat()
    {
        return string.Format("Hello {0}, you are {1}", "World", 42);
    }

    public static string StringFormatSingle()
    {
        return string.Format("Value: {0}", 123);
    }

    public static int StringIndexOf()
    {
        string s = "Hello World";
        return s.IndexOf('W'); // 6
    }

    public static bool StringContains()
    {
        string s = "Hello World";
        return s.Contains('W'); // true
    }

    public static bool StringStartsWith()
    {
        string s = "Hello World";
        return s.StartsWith("Hello"); // true
    }

    public static string StringToUpper()
    {
        string s = "hello";
        return s.ToUpper(); // "HELLO"
    }

    public static string StringTrim()
    {
        string s = "  hello  ";
        return s.Trim(); // "hello"
    }

    public static string StringReplace()
    {
        string s = "Hello World";
        return s.Replace('o', '0'); // "Hell0 W0rld"
    }

    public static int StringLastIndexOf()
    {
        string s = "Hello World";
        return s.LastIndexOf('l'); // 9
    }

    // ── System.IO ──────────────────────────────────────

    public static bool FileWriteAndRead()
    {
        string path = "test_io_temp.txt";
        System.IO.File.WriteAllText(path, "Hello, File!");
        string content = System.IO.File.ReadAllText(path);
        System.IO.File.Delete(path);
        return content == "Hello, File!";
    }

    public static bool FileExists()
    {
        string path = "test_io_exists.txt";
        System.IO.File.WriteAllText(path, "exists");
        bool exists = System.IO.File.Exists(path);
        System.IO.File.Delete(path);
        bool notExists = !System.IO.File.Exists(path);
        return exists && notExists;
    }

    public static string PathCombine()
    {
        return System.IO.Path.Combine("folder", "file.txt");
    }

    public static string PathGetFileName()
    {
        return System.IO.Path.GetFileName("/some/dir/file.txt");
    }

    public static string PathGetExtension()
    {
        return System.IO.Path.GetExtension("file.cs");
    }

    public static string PathGetDirectoryName()
    {
        return System.IO.Path.GetDirectoryName("/some/dir/file.txt");
    }
}

// Custom attribute for testing
[AttributeUsage(AttributeTargets.All)]
public class DescriptionAttribute : Attribute
{
    public string Text { get; }
    public DescriptionAttribute(string text) { Text = text; }
}

// Enum for attribute testing
public enum Severity { Low = 0, Medium = 1, High = 2, Critical = 3 }

// Attribute with Type parameter
[AttributeUsage(AttributeTargets.All)]
public class TypeRefAttribute : Attribute
{
    public Type TargetType { get; }
    public TypeRefAttribute(Type targetType) { TargetType = targetType; }
}

// Attribute with enum parameter
[AttributeUsage(AttributeTargets.All)]
public class SeverityAttribute : Attribute
{
    public Severity Level { get; }
    public SeverityAttribute(Severity level) { Level = level; }
}

// Attribute with array parameter
[AttributeUsage(AttributeTargets.All)]
public class TagsAttribute : Attribute
{
    public string[] Tags { get; }
    public TagsAttribute(params string[] tags) { Tags = tags; }
}

// Type with custom attributes
[Obsolete("Use NewClass instead")]
[Description("A test class with attributes")]
[TypeRef(typeof(string))]
[Severity(Severity.High)]
[Tags("important", "test", "example")]
public class AttributeTestClass
{
    [Obsolete("Use NewField")]
    public int OldField;

    [Description("Important method")]
    [TypeRef(typeof(int))]
    public void AnnotatedMethod() { }
}

// Multi-dimensional array test class
public class MdArrayTest
{
    public static int[,] Create2D()
    {
        var arr = new int[3, 4];
        arr[0, 0] = 1;
        arr[1, 2] = 42;
        arr[2, 3] = 99;
        return arr;
    }

    public static int Get2D(int[,] arr, int i, int j)
    {
        return arr[i, j];
    }

    public static void Set2D(int[,] arr, int i, int j, int value)
    {
        arr[i, j] = value;
    }

    public static int GetTotalLength(int[,] arr)
    {
        return arr.Length;
    }

    public static int GetDimLength(int[,] arr, int dim)
    {
        return arr.GetLength(dim);
    }

    public static int GetRank(int[,] arr)
    {
        return arr.Rank;
    }

    public static string[,] Create2DString()
    {
        var arr = new string[2, 2];
        arr[0, 0] = "hello";
        arr[1, 1] = "world";
        return arr;
    }
}

// Default Interface Methods (DIM) test
public interface IGreeter
{
    // Abstract method — must be implemented by class
    string GetName();

    // Default method — provides a default implementation
    string Greet()
    {
        return "Hello, " + GetName() + "!";
    }

    // Default method that returns a constant
    int Version() => 1;
}

public interface ILogger2
{
    // Default method only — no abstract members
    string Log(string message) => "[LOG] " + message;
}

// Class that uses default Greet() but implements GetName()
public class DefaultGreeterUser : IGreeter
{
    public string GetName() => "World";
    // Greet() uses default from IGreeter
}

// Class that overrides the default Greet()
public class CustomGreeterUser : IGreeter
{
    public string GetName() => "Custom";
    public string Greet() => "Hey there, " + GetName() + "!";
    public int Version() => 2;  // Override default
}

// Class implementing interface with only default methods
public class LoggerUser : ILogger2
{
    // Uses all defaults from ILogger2
}

public static class DIMTest
{
    public static string DefaultGreet()
    {
        IGreeter g = new DefaultGreeterUser();
        return g.Greet();
    }

    public static string CustomGreet()
    {
        IGreeter g = new CustomGreeterUser();
        return g.Greet();
    }

    public static int DefaultVersion()
    {
        IGreeter g = new DefaultGreeterUser();
        return g.Version();
    }

    public static int OverriddenVersion()
    {
        IGreeter g = new CustomGreeterUser();
        return g.Version();
    }

    public static string DefaultLog()
    {
        ILogger2 l = new LoggerUser();
        return l.Log("test");
    }
}

// Span<T> test class
public static class SpanTest
{
    public static int SpanFromArray()
    {
        int[] arr = new int[] { 10, 20, 30, 40, 50 };
        Span<int> span = new Span<int>(arr);
        return span.Length;
    }

    public static int SpanGetItem()
    {
        int[] arr = new int[] { 10, 20, 30, 40, 50 };
        Span<int> span = new Span<int>(arr);
        return span[2]; // should be 30
    }

    public static int SpanSlice()
    {
        int[] arr = new int[] { 10, 20, 30, 40, 50 };
        Span<int> span = new Span<int>(arr);
        Span<int> sliced = span.Slice(1, 3);
        return sliced.Length; // should be 3
    }

    public static int ReadOnlySpanLength()
    {
        int[] arr = new int[] { 1, 2, 3 };
        ReadOnlySpan<int> span = new ReadOnlySpan<int>(arr);
        return span.Length;
    }

    public static unsafe int SpanFromPointer()
    {
        int* buf = stackalloc int[4];
        buf[0] = 100;
        buf[1] = 200;
        Span<int> span = new Span<int>(buf, 4);
        return span[0]; // should be 100
    }
}

// P/Invoke test class
// Struct for P/Invoke marshaling test
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
public struct Point2D
{
    public int X;
    public int Y;
}

// Delegate for P/Invoke callback test
public delegate int CompareCallback(IntPtr a, IntPtr b);

public static class PInvokeTest
{
    [System.Runtime.InteropServices.DllImport("msvcrt.dll", EntryPoint = "abs")]
    public static extern int NativeAbs(int value);

    [System.Runtime.InteropServices.DllImport("msvcrt.dll", EntryPoint = "strlen")]
    public static extern int NativeStrLen(string s);

    // P/Invoke with delegate callback (qsort uses comparison function pointer)
    [System.Runtime.InteropServices.DllImport("msvcrt.dll", EntryPoint = "qsort")]
    public static extern void NativeQSort(IntPtr baseAddr, int numElements, int elementSize, CompareCallback compare);
}

// Generic Variance test classes
public interface ICovariant<out T>
{
    T Get();
}

public interface IContravariant<in T>
{
    void Accept(T item);
}

public interface IInvariant<T>
{
    T Process(T item);
}

public class CovariantString : ICovariant<string>
{
    public string Get() => "hello";
}

public class ContravariantObject : IContravariant<object>
{
    public void Accept(object item) { }
}

public static class VarianceTest
{
    public static ICovariant<string> GetCovariantString() => new CovariantString();
    public static IContravariant<object> GetContravariantObject() => new ContravariantObject();
}

// ===== Iterator / IEnumerable test classes =====

// Simple custom enumerator (manual implementation)
public class SimpleRange : IEnumerable<int>
{
    private readonly int _start;
    private readonly int _count;

    public SimpleRange(int start, int count)
    {
        _start = start;
        _count = count;
    }

    public IEnumerator<int> GetEnumerator() => new RangeEnumerator(_start, _count);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private class RangeEnumerator : IEnumerator<int>
    {
        private readonly int _start;
        private readonly int _count;
        private int _index;

        public RangeEnumerator(int start, int count)
        {
            _start = start;
            _count = count;
            _index = -1;
        }

        public int Current => _start + _index;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            _index++;
            return _index < _count;
        }

        public void Reset() { _index = -1; }

        public void Dispose() { }
    }
}

// Iterator using yield return
public static class IteratorHelper
{
    public static IEnumerable<int> GetNumbers()
    {
        yield return 10;
        yield return 20;
        yield return 30;
    }

    public static IEnumerable<int> CountUp(int start, int count)
    {
        for (int i = 0; i < count; i++)
        {
            yield return start + i;
        }
    }

    public static IEnumerable<string> FilterStrings(string[] items, int minLength)
    {
        foreach (var item in items)
        {
            if (item.Length >= minLength)
                yield return item;
        }
    }
}

public static class IteratorTest
{
    // foreach over manual IEnumerable<int>
    public static int SumRange(int start, int count)
    {
        var range = new SimpleRange(start, count);
        int sum = 0;
        foreach (var n in range)
        {
            sum += n;
        }
        return sum;
    }

    // foreach over yield return iterator
    public static int SumYield()
    {
        int sum = 0;
        foreach (var n in IteratorHelper.GetNumbers())
        {
            sum += n;
        }
        return sum;
    }

    // foreach over yield return with parameters
    public static int SumCountUp()
    {
        int sum = 0;
        foreach (var n in IteratorHelper.CountUp(1, 5))
        {
            sum += n;
        }
        return sum; // 1+2+3+4+5 = 15
    }

    // yield return with filtering
    public static string JoinFiltered()
    {
        var items = new string[] { "hi", "hello", "hey", "greetings" };
        string result = "";
        foreach (var s in IteratorHelper.FilterStrings(items, 4))
        {
            if (result.Length > 0) result = result + ",";
            result = result + s;
        }
        return result; // "hello,greetings"
    }
}

// ── IAsyncEnumerable / await foreach ─────────────────────
public static class AsyncEnumerableHelper
{
    // Simple async iterator yielding 1..n
    public static async IAsyncEnumerable<int> RangeAsync(int start, int count)
    {
        for (int i = 0; i < count; i++)
        {
            await Task.CompletedTask; // simulate async work
            yield return start + i;
        }
    }

    // await foreach consumer
    public static async Task<int> SumRangeAsync()
    {
        int sum = 0;
        await foreach (var n in RangeAsync(1, 5))
        {
            sum += n;
        }
        return sum; // 1+2+3+4+5 = 15
    }

    // Async iterator with filtering
    public static async IAsyncEnumerable<int> FilterAsync(int[] items, int min)
    {
        foreach (var item in items)
        {
            await Task.CompletedTask;
            if (item >= min)
                yield return item;
        }
    }

    public static async Task<string> JoinFilteredAsync()
    {
        var items = new int[] { 1, 5, 3, 8, 2, 7 };
        string result = "";
        await foreach (var n in FilterAsync(items, 5))
        {
            if (result.Length > 0) result = result + ",";
            result = result + n.ToString();
        }
        return result; // "5,8,7"
    }
}
