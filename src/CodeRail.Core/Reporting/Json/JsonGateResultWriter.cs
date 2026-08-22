using System.Text.Json;
using System.Text.Json.Serialization;
using CodeRail.Policy;

namespace CodeRail.Reporting.Json;

/// <summary>The primary agent-facing output format (<c>docs/HIGH_LEVEL_PLAN.md</c> §20/§15) -
/// plain <see cref="GateResult"/> serialization, camelCase, matching CodeGuard's own
/// <c>JsonViolationReporter</c> conventions.</summary>
public sealed class JsonGateResultWriter : IGateResultWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Format => "json";

    public async Task WriteAsync(GateResult result, TextWriter writer, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(result, Options);
        await writer.WriteLineAsync(json.AsMemory(), cancellationToken);
    }
}
