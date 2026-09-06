namespace BrokenTestProject;

public class BrokenTests
{
    [Fact]
    public void ThisDoesNotCompile()
    {
        Assert.Equal(1, NotAThing);
    }
}
