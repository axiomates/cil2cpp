using System;

public class Calculator
{
    private int _result;

    public int Add(int a, int b)
    {
        return a + b;
    }

    public void SetResult(int value)
    {
        _result = value;
    }

    public int GetResult()
    {
        return _result;
    }
}

public class Program
{
    public static void Main()
    {
        Console.WriteLine("Hello, CIL2CPP!");

        var calc = new Calculator();
        int sum = calc.Add(10, 20);
        Console.WriteLine(sum);

        calc.SetResult(42);
        Console.WriteLine(calc.GetResult());
    }
}
