using System.Text.Json;
using CodeRail.Evidence;
using CodeRail.Policy;
using CodeRail.Reporting.Json;

namespace CodeRail.Reporting.Tests.Json;

public class JsonGateResultWriterTests
{
    private readonly JsonGateResultWriter _writer = new();

    [Fact]
    public async Task WriteAsync_ProducesValidCamelCaseJson_WithStringEnums()
    {
        var finding = new Finding("build-failure", Severity.Error, "CS0103", "Foo.cs", 12, null);
        var build = new ToolResult(
            ToolIds.DotnetBuild, ValidationStatus.Failed, [finding],
            new Dictionary<string, object> { ["solutionsBuilt"] = 1 }, [], TimeSpan.FromSeconds(3));
        var evaluatedAt = new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        var gate = new GateResult(ValidationStatus.Failed, [build], [finding], evaluatedAt);

        var stringWriter = new StringWriter();
        await _writer.WriteAsync(gate, stringWriter);
        var json = stringWriter.ToString();

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.Equal("failed", root.GetProperty("status").GetString());
        Assert.Equal("dotnet-build", root.GetProperty("tools")[0].GetProperty("tool").GetString());
        Assert.Equal("failed", root.GetProperty("tools")[0].GetProperty("status").GetString());
        Assert.Equal(1, root.GetProperty("tools")[0].GetProperty("metrics").GetProperty("solutionsBuilt").GetInt32());
        Assert.Equal("error", root.GetProperty("blockingFindings")[0].GetProperty("severity").GetString());
        Assert.Equal("CS0103", root.GetProperty("blockingFindings")[0].GetProperty("message").GetString());
    }

    [Fact]
    public async Task WriteAsync_OmitsNullProperties()
    {
        var finding = new Finding("test-failure", Severity.Error, "boom", null, null, null);
        var gate = new GateResult(ValidationStatus.Failed, [], [finding], DateTimeOffset.UtcNow);

        var stringWriter = new StringWriter();
        await _writer.WriteAsync(gate, stringWriter);
        var json = stringWriter.ToString();

        using var document = JsonDocument.Parse(json);
        var findingElement = document.RootElement.GetProperty("blockingFindings")[0];
        Assert.False(findingElement.TryGetProperty("file", out _));
        Assert.False(findingElement.TryGetProperty("line", out _));
        Assert.False(findingElement.TryGetProperty("ruleId", out _));
    }
}
