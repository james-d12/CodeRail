namespace LowCoverage.Lib;

// Only Add() is exercised by LowCoverage.Tests - Subtract/Multiply/Divide are deliberately left
// uncovered so the resulting line coverage percentage sits well below a strict threshold.
public class Calculator
{
    public int Add(int a, int b) => a + b;

    public int Subtract(int a, int b) => a - b;

    public int Multiply(int a, int b) => a * b;

    public int Divide(int a, int b) => a / b;
}
