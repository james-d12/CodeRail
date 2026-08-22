namespace FailingTestsProject;

public class FailingTests
{
    [Fact]
    public void AlwaysPasses() => Assert.True(true);

    [Fact]
    public void AlwaysFails() => Assert.True(false, "intentional failure for CodeRail fixture testing");
}
