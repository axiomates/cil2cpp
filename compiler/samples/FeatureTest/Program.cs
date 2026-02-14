using System;

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

// Record struct (value type record — no <Clone>$, uses value copy semantics)
public record struct PointRecord(int X, int Y);

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
        TestValueTuple();
        TestRecord();
        TestRecordStruct();
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
}
