using System.Text.Json;
using CodeRail.Evidence;
using CodeRail.Policy;
using CodeRail.Reporting.Sarif;

namespace CodeRail.Reporting.Tests.Sarif;

public class SarifGateResultWriterTests
{
    private readonly SarifGateResultWriter _writer = new();

    [Fact]
    public async Task WriteAsync_ProducesA210SarifDocument_WithASchemaAndVersion()
    {
        var gate = new GateResult(ValidationStatus.Passed, [], [], DateTimeOffset.UtcNow);

        var json = await Write(gate);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal(
            "https://raw.githubusercontent.com/oasis-tcs/sarif-spec/master/Schemata/sarif-schema-2.1.0.json",
            root.GetProperty("$schema").GetString());
        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        Assert.Equal("CodeRail", root.GetProperty("runs")[0].GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());
    }

    [Fact]
    public async Task WriteAsync_EmitsEveryFinding_NotJustBlockingOnes()
    {
        var blocking = new Finding("build-failure", Severity.Error, "CS0103", "Foo.cs", 12, null);
        var informational = new Finding("style", Severity.Info, "nit", "Bar.cs", 3, "STYLE-001");
        var build = new ToolResult(
            ToolIds.DotnetBuild, ValidationStatus.Failed, [blocking], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var codeGuard = new ToolResult(
            ToolIds.CodeGuard, ValidationStatus.Passed, [informational], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Failed, [build, codeGuard], [blocking], DateTimeOffset.UtcNow);

        var json = await Write(gate);

        using var document = JsonDocument.Parse(json);
        var results = document.RootElement.GetProperty("runs")[0].GetProperty("results");
        Assert.Equal(2, results.GetArrayLength());
        Assert.Contains(results.EnumerateArray(), r => r.GetProperty("message").GetProperty("text").GetString() == "CS0103");
        Assert.Contains(results.EnumerateArray(), r => r.GetProperty("message").GetProperty("text").GetString() == "nit");
    }

    [Theory]
    [InlineData(Severity.Info, "note")]
    [InlineData(Severity.Warning, "warning")]
    [InlineData(Severity.Error, "error")]
    [InlineData(Severity.Critical, "error")]
    public async Task WriteAsync_MapsSeverityToSarifLevel(Severity severity, string expectedLevel)
    {
        var finding = new Finding("some-rule", severity, "message", null, null, null);
        var tool = new ToolResult(ToolIds.CodeGuard, ValidationStatus.Failed, [finding], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Failed, [tool], [], DateTimeOffset.UtcNow);

        var json = await Write(gate);

        using var document = JsonDocument.Parse(json);
        var result = document.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        Assert.Equal(expectedLevel, result.GetProperty("level").GetString());
    }

    [Fact]
    public async Task WriteAsync_UsesRuleId_FallingBackToFindingType_WhenRuleIdIsNull()
    {
        var withRuleId = new Finding("architecture", Severity.Error, "layering violation", null, null, "ARCH-001");
        var withoutRuleId = new Finding("test-failure", Severity.Error, "boom", null, null, null);
        var tool = new ToolResult(
            ToolIds.CodeGuard, ValidationStatus.Failed, [withRuleId, withoutRuleId], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Failed, [tool], [], DateTimeOffset.UtcNow);

        var json = await Write(gate);

        using var document = JsonDocument.Parse(json);
        var results = document.RootElement.GetProperty("runs")[0].GetProperty("results");
        Assert.Contains(results.EnumerateArray(), r => r.GetProperty("ruleId").GetString() == "ARCH-001");
        Assert.Contains(results.EnumerateArray(), r => r.GetProperty("ruleId").GetString() == "test-failure");

        var rules = document.RootElement.GetProperty("runs")[0].GetProperty("tool").GetProperty("driver").GetProperty("rules");
        Assert.Contains(rules.EnumerateArray(), r => r.GetProperty("id").GetString() == "ARCH-001");
        Assert.Contains(rules.EnumerateArray(), r => r.GetProperty("id").GetString() == "test-failure");
    }

    [Fact]
    public async Task WriteAsync_IncludesALocation_WhenTheFindingHasAFileAndLine()
    {
        var finding = new Finding("build-failure", Severity.Error, "CS0103", "Foo.cs", 12, null);
        var tool = new ToolResult(ToolIds.DotnetBuild, ValidationStatus.Failed, [finding], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Failed, [tool], [], DateTimeOffset.UtcNow);

        var json = await Write(gate);

        using var document = JsonDocument.Parse(json);
        var location = document.RootElement.GetProperty("runs")[0].GetProperty("results")[0].GetProperty("locations")[0];
        Assert.Equal("Foo.cs", location.GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString());
        Assert.Equal(12, location.GetProperty("physicalLocation").GetProperty("region").GetProperty("startLine").GetInt32());
    }

    [Fact]
    public async Task WriteAsync_OmitsLocations_WhenTheFindingHasNoFile()
    {
        var finding = new Finding("test-failure", Severity.Error, "boom", null, null, null);
        var tool = new ToolResult(ToolIds.DotnetTest, ValidationStatus.Failed, [finding], new Dictionary<string, object>(), [], TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Failed, [tool], [], DateTimeOffset.UtcNow);

        var json = await Write(gate);

        using var document = JsonDocument.Parse(json);
        var result = document.RootElement.GetProperty("runs")[0].GetProperty("results")[0];
        Assert.False(result.TryGetProperty("locations", out _));
    }

    private async Task<string> Write(GateResult gate)
    {
        var stringWriter = new StringWriter();
        await _writer.WriteAsync(gate, stringWriter);
        return stringWriter.ToString();
    }
}
