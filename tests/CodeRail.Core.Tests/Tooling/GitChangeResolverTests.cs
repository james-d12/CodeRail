using CodeRail.Execution;
using CodeRail.Tooling.Tests.Executors;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodeRail.Tooling.Tests;

public class GitChangeResolverTests
{
    [Fact]
    public async Task ResolveAsync_UnionsCommittedStagedAndWorkingTreeChanges()
    {
        var runner = new StubProcessRunner((_, arguments, _) => arguments switch
        {
            "merge-base \"origin/main\" HEAD" => Ok("abc123\n"),
            "diff --name-only \"abc123\" HEAD" => Ok("Lib/A.cs\nLib/B.cs\n"),
            "diff --name-only" => Ok("Lib/C.cs\n"),
            "diff --name-only --cached" => Ok("Lib/D.cs\n"),
            _ => throw new InvalidOperationException($"Unexpected git invocation: {arguments}")
        });

        var changes = await GitChangeResolver.ResolveAsync(runner, "/repo", "origin/main", NullLogger.Instance, CancellationToken.None);

        Assert.Equal(["Lib/A.cs", "Lib/B.cs", "Lib/C.cs", "Lib/D.cs"], changes.ChangedFiles);
    }

    [Fact]
    public async Task ResolveAsync_DeduplicatesAFileChangedInMoreThanOneDiff()
    {
        var runner = new StubProcessRunner((_, arguments, _) => arguments switch
        {
            "merge-base \"origin/main\" HEAD" => Ok("abc123\n"),
            "diff --name-only \"abc123\" HEAD" => Ok("Lib/A.cs\n"),
            "diff --name-only" => Ok("Lib/A.cs\n"),
            "diff --name-only --cached" => Ok(""),
            _ => throw new InvalidOperationException($"Unexpected git invocation: {arguments}")
        });

        var changes = await GitChangeResolver.ResolveAsync(runner, "/repo", "origin/main", NullLogger.Instance, CancellationToken.None);

        Assert.Equal(["Lib/A.cs"], changes.ChangedFiles);
    }

    [Fact]
    public async Task ResolveAsync_Throws_WhenMergeBaseProducesNoOutput()
    {
        var runner = StubProcessRunner.Returning(Ok(""));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => GitChangeResolver.ResolveAsync(runner, "/repo", "does-not-exist", NullLogger.Instance, CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAsync_Throws_WhenAGitCommandFails()
    {
        var runner = new StubProcessRunner((_, arguments, _) => arguments.StartsWith("merge-base")
            ? Ok("abc123\n")
            : new ProcessResult(128, "", "fatal: bad revision", TimeSpan.Zero, false));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => GitChangeResolver.ResolveAsync(runner, "/repo", "origin/main", NullLogger.Instance, CancellationToken.None));
        Assert.Contains("fatal: bad revision", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_DerivesChangedProjects_ByWalkingUpForTheNearestCsproj()
    {
        var repoRoot = Directory.CreateTempSubdirectory("coderail-changeset-").FullName;
        try
        {
            var projectDir = Directory.CreateDirectory(Path.Combine(repoRoot, "src", "Lib"));
            File.WriteAllText(Path.Combine(projectDir.FullName, "Lib.csproj"), "<Project />");
            Directory.CreateDirectory(Path.Combine(projectDir.FullName, "Sub"));

            var runner = new StubProcessRunner((_, arguments, _) => arguments switch
            {
                "merge-base \"origin/main\" HEAD" => Ok("abc123\n"),
                "diff --name-only \"abc123\" HEAD" => Ok("src/Lib/Sub/Nested.cs\n"),
                _ => Ok("")
            });

            var changes = await GitChangeResolver.ResolveAsync(runner, repoRoot, "origin/main", NullLogger.Instance, CancellationToken.None);

            var project = Assert.Single(changes.ChangedProjects);
            Assert.Equal(Path.Combine(projectDir.FullName, "Lib.csproj"), project);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ResolveAsync_AgainstARealGitRepository_ResolvesCommittedStagedAndWorkingTreeChanges()
    {
        // The StubProcessRunner-based tests above cover argument shape and error handling; git
        // diff *behaviour* (merge-base, three-way union, ordering) is only meaningfully verified
        // against a real repository.
        var repoRoot = Directory.CreateTempSubdirectory("coderail-real-git-").FullName;
        try
        {
            var runner = new ProcessRunner(NullLogger<ProcessRunner>.Instance);

            await RunGit(runner, repoRoot, "init");
            await RunGit(runner, repoRoot, "config user.email test@example.com");
            await RunGit(runner, repoRoot, "config user.name \"CodeRail Test\"");
            await RunGit(runner, repoRoot, "config commit.gpgsign false");

            File.WriteAllText(Path.Combine(repoRoot, "Existing.cs"), "// v1\n");
            File.WriteAllText(Path.Combine(repoRoot, "Tracked.cs"), "// v1\n");
            await RunGit(runner, repoRoot, "add .");
            await RunGit(runner, repoRoot, "commit -m initial");
            var initialSha = (await RunGit(runner, repoRoot, "rev-parse HEAD")).StandardOutput.Trim();

            // Committed since the base revision.
            File.WriteAllText(Path.Combine(repoRoot, "Existing.cs"), "// v2\n");
            await RunGit(runner, repoRoot, "add Existing.cs");
            await RunGit(runner, repoRoot, "commit -m second");

            // Modified in the working tree, never staged.
            File.WriteAllText(Path.Combine(repoRoot, "Tracked.cs"), "// v2 unstaged\n");

            // A brand-new file, staged but not committed.
            File.WriteAllText(Path.Combine(repoRoot, "Staged.cs"), "// new\n");
            await RunGit(runner, repoRoot, "add Staged.cs");

            var changes = await GitChangeResolver.ResolveAsync(runner, repoRoot, initialSha, NullLogger.Instance, CancellationToken.None);

            Assert.Equal(["Existing.cs", "Staged.cs", "Tracked.cs"], changes.ChangedFiles);
        }
        finally
        {
            Directory.Delete(repoRoot, recursive: true);
        }
    }

    private static async Task<ProcessResult> RunGit(IProcessRunner runner, string repoRoot, string arguments)
    {
        var result = await runner.RunAsync("git", arguments, repoRoot, timeout: TimeSpan.FromSeconds(30));
        Assert.True(result.ExitCode == 0, $"'git {arguments}' failed: {result.StandardError}");
        return result;
    }

    private static ProcessResult Ok(string standardOutput) => new(0, standardOutput, "", TimeSpan.Zero, false);
}
