using MathLib;

public class Adder : ICalculator
{
    public int Calculate(int a, int b) => MathUtils.Add(a, b);
}

public class Program
{
    public static void Main()
    {
        // Cross-assembly static method call
        int sum = MathUtils.Add(10, 20);
        Console.WriteLine(sum); // 30

        // Cross-assembly object creation + instance methods
        var counter = new Counter();
        counter.Increment();
        counter.Increment();
        counter.Increment();
        Console.WriteLine(counter.GetCount()); // 3

        // Cross-assembly interface implementation
        ICalculator calc = new Adder();
        Console.WriteLine(calc.Calculate(3, 4)); // 7
    }
}
