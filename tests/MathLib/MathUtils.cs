namespace MathLib;

public class MathUtils
{
    public static int Add(int a, int b) => a + b;
    public static int Multiply(int a, int b) => a * b;
    public static int Subtract(int a, int b) => a - b;
}

public class Counter
{
    private int _count;
    public void Increment() { _count++; }
    public void Decrement() { _count--; }
    public int GetCount() { return _count; }
}

public interface ICalculator
{
    int Calculate(int a, int b);
}
