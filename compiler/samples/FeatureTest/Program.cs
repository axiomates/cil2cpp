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
}
