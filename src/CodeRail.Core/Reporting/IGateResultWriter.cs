using CodeRail.Policy;

namespace CodeRail.Reporting;

/// <summary>Renders a <see cref="GateResult"/> in one output format. See
/// <c>docs/HIGH_LEVEL_PLAN.md</c> §20 (Output Formats).</summary>
public interface IGateResultWriter
{
    string Format { get; }

    Task WriteAsync(GateResult result, TextWriter writer, CancellationToken cancellationToken = default);
}
