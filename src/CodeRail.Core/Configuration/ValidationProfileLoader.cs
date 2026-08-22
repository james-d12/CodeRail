using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CodeRail.Configuration;

/// <summary>Loads a <see cref="ValidationProfile"/> from YAML, either the built-in default
/// profile embedded in this assembly or an on-disk file supplied via <c>--profile</c>.</summary>
public static class ValidationProfileLoader
{
    private const string DefaultProfileResourceName = "CodeRail.Core.Configuration.Profiles.dotnet-default.yml";

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    /// <summary>The built-in profile shipped inside this assembly - used when a repository
    /// doesn't supply its own profile file.</summary>
    public static ValidationProfile LoadDefault()
    {
        using var stream = typeof(ValidationProfileLoader).Assembly.GetManifestResourceStream(DefaultProfileResourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{DefaultProfileResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        return Deserialize(reader, "<embedded dotnet-default>");
    }

    public static ValidationProfile LoadFromFile(string path)
    {
        using var reader = new StreamReader(path);
        return Deserialize(reader, path);
    }

    private static ValidationProfile Deserialize(TextReader reader, string source) =>
        Deserializer.Deserialize<ValidationProfile>(reader)
            ?? throw new InvalidOperationException($"'{source}' deserialized to an empty validation profile.");
}
