namespace CodeRail.Tooling.Tests;

public class SolutionFileLocatorTests
{
    [Fact]
    public void Resolve_ReturnsEmpty_WhenNoSolutionExistsAndNoneSpecified()
    {
        var emptyDir = Directory.CreateTempSubdirectory("coderail-locator-").FullName;
        try
        {
            var resolved = Tooling.SolutionFileLocator.Resolve(emptyDir, []);

            Assert.Empty(resolved);
        }
        finally
        {
            Directory.Delete(emptyDir, recursive: true);
        }
    }

    [Fact]
    public void Resolve_FindsSolutionRecursively_SkippingExcludedDirectories()
    {
        var root = Directory.CreateTempSubdirectory("coderail-locator-").FullName;
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(root, "nested"));
            File.WriteAllText(Path.Combine(nested.FullName, "Nested.sln"), string.Empty);

            var excluded = Directory.CreateDirectory(Path.Combine(root, "bin"));
            File.WriteAllText(Path.Combine(excluded.FullName, "ShouldBeSkipped.sln"), string.Empty);

            var resolved = Tooling.SolutionFileLocator.Resolve(root, []);

            Assert.Single(resolved);
            Assert.EndsWith("Nested.sln", resolved[0]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Resolve_Throws_WhenExplicitSolutionDoesNotExist()
    {
        var root = Directory.CreateTempSubdirectory("coderail-locator-").FullName;
        try
        {
            Assert.Throws<FileNotFoundException>(() => Tooling.SolutionFileLocator.Resolve(root, ["missing.sln"]));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
