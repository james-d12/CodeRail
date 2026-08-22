namespace CodeRail.Evidence;

/// <summary>A file produced by a tool run that's worth keeping around (e.g. a TRX file, a
/// coverage report, a mutation report) - referenced by path rather than embedded.</summary>
public sealed record Artifact(string Name, string Path, string ContentType);
