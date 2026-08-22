using LowCoverage.Lib;

namespace LowCoverage.Tests;

public class CalculatorTests
{
    [Fact]
    public void Add_ReturnsSum() => Assert.Equal(5, new Calculator().Add(2, 3));
}
