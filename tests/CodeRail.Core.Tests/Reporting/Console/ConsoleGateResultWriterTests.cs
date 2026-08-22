using CodeRail.Evidence;
using CodeRail.Policy;
using CodeRail.Reporting.Console;

namespace CodeRail.Reporting.Tests.Console;

public class ConsoleGateResultWriterTests
{
    private readonly ConsoleGateResultWriter _writer = new();

    [Fact]
    public async Task WriteAsync_RendersPassedVerdict_AndPerToolPassLines_WithNoFindingsSection()
    {
        var build = new ToolResult(ToolIds.DotnetBuild, ValidationStatus.Passed, [], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var test = new ToolResult(ToolIds.DotnetTest, ValidationStatus.Passed, [], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Passed, [build, test], [], DateTimeOffset.UtcNow);

        var output = await Render(gate);

        Assert.Contains("QUALITY GATE: PASSED", output);
        Assert.Contains("Build: PASS", output);
        Assert.Contains("Tests: PASS", output);
        Assert.DoesNotContain("Blocking findings:", output);
    }

    [Fact]
    public async Task WriteAsync_RendersFailedVerdict_WithBlockingFindingsAndLocation()
    {
        var finding = new Finding("architecture", Severity.Critical, "Domain references Infrastructure", "OrderService.cs", 42, "ARCH-001");
        var codeGuard = new ToolResult(ToolIds.CodeGuard, ValidationStatus.Failed, [finding], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Failed, [codeGuard], [finding], DateTimeOffset.UtcNow);

        var output = await Render(gate);

        Assert.Contains("QUALITY GATE: FAILED", output);
        Assert.Contains("CodeGuard: FAIL", output);
        Assert.Contains("Blocking findings:", output);
        Assert.Contains("Domain references Infrastructure", output);
        Assert.Contains("(OrderService.cs:42)", output);
        Assert.Contains("Action required:", output);
    }

    [Fact]
    public async Task WriteAsync_OmitsLocationSuffix_WhenFindingHasNoFile()
    {
        var finding = new Finding("coverage-below-threshold", Severity.Error, "Line coverage 61% is below the required minimum 80%.", null, null, null);
        var coverage = new ToolResult(ToolIds.Coverage, ValidationStatus.Passed, [finding], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Failed, [coverage], [finding], DateTimeOffset.UtcNow);

        var output = await Render(gate);

        Assert.Contains("- Line coverage 61% is below the required minimum 80%.\n", output.Replace("\r\n", "\n"));
    }

    private async Task<string> Render(GateResult gate)
    {
        var stringWriter = new StringWriter();
        await _writer.WriteAsync(gate, stringWriter);
        return stringWriter.ToString();
    }
}
