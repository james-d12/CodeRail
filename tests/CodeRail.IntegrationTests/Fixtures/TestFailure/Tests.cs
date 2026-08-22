namespace TestFailure;

public class Tests
{
    [Fact]
    public void AlwaysPasses() => Assert.True(true);

    [Fact]
    public void AlwaysFails() => Assert.Fail("intentional failure for CodeRail integration testing");
}
