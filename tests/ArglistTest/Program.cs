using System;

class Program
{
    // ===== Test 1: Basic count =====
    static void PrintCount(__arglist)
    {
        ArgIterator args = new ArgIterator(__arglist);
        Console.WriteLine(args.GetRemainingCount());
        args.End();
    }

    // ===== Test 2: Fixed params + varargs =====
    static void FixedPlusVarargs(string prefix, int fixedVal, __arglist)
    {
        ArgIterator args = new ArgIterator(__arglist);
        Console.Write(prefix);
        Console.Write(fixedVal);
        Console.Write(" varargs=");
        Console.WriteLine(args.GetRemainingCount());
        args.End();
    }

    // ===== Test 3: Iterate and sum int values =====
    static int SumInts(__arglist)
    {
        ArgIterator args = new ArgIterator(__arglist);
        int sum = 0;
        while (args.GetRemainingCount() > 0)
        {
            TypedReference tr = args.GetNextArg();
            sum += __refvalue(tr, int);
        }
        args.End();
        return sum;
    }

    // ===== Test 4: Zero varargs =====
    static void ZeroVarargs(__arglist)
    {
        ArgIterator args = new ArgIterator(__arglist);
        Console.WriteLine(args.GetRemainingCount());
        args.End();
    }

    // ===== Test 5: mkrefany + refanyval (TypedReference write-back) =====
    static void TestMakeRef()
    {
        int x = 42;
        TypedReference tr = __makeref(x);
        __refvalue(tr, int) = 100;
        Console.WriteLine(x);
    }

    // ===== Test 6: Single vararg =====
    static void SingleVararg(__arglist)
    {
        ArgIterator args = new ArgIterator(__arglist);
        Console.Write(args.GetRemainingCount());
        TypedReference tr = args.GetNextArg();
        Console.Write(" val=");
        Console.WriteLine(__refvalue(tr, int));
        args.End();
    }

    static void Main()
    {
        // Test 1: Basic count (3 varargs)
        PrintCount(__arglist(1, 2, 3));

        // Test 2: Fixed params + 2 varargs
        FixedPlusVarargs("fixed=", 99, __arglist(10, 20));

        // Test 3: Sum 3 ints via iteration
        Console.WriteLine(SumInts(__arglist(10, 20, 30)));

        // Test 4: Zero varargs
        ZeroVarargs(__arglist());

        // Test 5: TypedReference write-back
        TestMakeRef();

        // Test 6: Single vararg with value extraction
        SingleVararg(__arglist(42));
    }
}
