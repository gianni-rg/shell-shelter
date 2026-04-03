using System.Text.Json;

namespace ShellShelter.Core.Config;

/// <summary>
/// Parses ShellShelter JSON configuration into shell policies.
/// </summary>
public sealed class JsonConfigParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// Parses a JSON configuration document.
    /// </summary>
    /// <param name="json">The JSON content.</param>
    /// <returns>The parsed shell policy pair.</returns>
    public ShellPolicyPair Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("JSON content cannot be null or whitespace.", nameof(json));
        }

        JsonConfigDocument document = JsonSerializer.Deserialize<JsonConfigDocument>(json, SerializerOptions)
            ?? throw new JsonException("The JSON configuration could not be parsed.");

        return new ShellPolicyPair(
            CreatePolicy(document.Bash),
            CreatePolicy(document.PowerShell));
    }

    private static ShellPolicy CreatePolicy(JsonShellPolicySection? section)
    {
        return new ShellPolicy(ParseCmds(section?.OkCmds), section?.OkDests);
    }

    private static IEnumerable<CmdSpec> ParseCmds(IEnumerable<string>? specs)
    {
        if (specs is null)
        {
            yield break;
        }

        foreach (string? spec in specs)
        {
            if (string.IsNullOrWhiteSpace(spec))
            {
                continue;
            }

            yield return CmdSpec.FromStr(spec.Trim());
        }
    }

    private sealed class JsonConfigDocument
    {
        public JsonShellPolicySection? Bash { get; init; }

        public JsonShellPolicySection? PowerShell { get; init; }
    }

    private sealed class JsonShellPolicySection
    {
        public List<string>? OkCmds { get; init; }

        public List<string>? OkDests { get; init; }
    }
}
