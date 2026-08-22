namespace CodeRail.Tooling;

/// <summary>Trims captured process output down to its last few lines, for embedding in a
/// <see cref="CodeRail.Evidence.Finding"/> message without dumping an entire build log.</summary>
internal static class OutputTail
{
    public static string Last(string output, int maxLines = 40)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length <= maxLines
            ? string.Join('\n', lines)
            : string.Join('\n', lines[^maxLines..]);
    }
}
