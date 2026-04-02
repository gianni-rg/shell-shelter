using ShellShelter.Core;

namespace ShellShelter.Core.Config;

/// <summary>
/// Parses Python safecmd-compatible INI configuration for the <c>[DEFAULT]</c> section.
/// </summary>
public sealed class IniConfigParser
{
    private const string DefaultOkDests = "./, /tmp";

    /// <summary>
    /// Parses an INI configuration document.
    /// </summary>
    /// <param name="ini">The INI content.</param>
    /// <returns>The parsed shell policy pair.</returns>
    public ShellPolicyPair Parse(string ini)
    {
        if (string.IsNullOrWhiteSpace(ini))
        {
            throw new ArgumentException("INI content cannot be null or whitespace.", nameof(ini));
        }

        Dictionary<string, List<string>> defaults = ParseDefaultSection(ini);
        string okDestsValue = GetJoinedValue(defaults, "ok_dests") ?? DefaultOkDests;
        string okCmdsValue = GetJoinedValue(defaults, "ok_cmds")
            ?? throw new KeyNotFoundException("The [DEFAULT] section must define 'ok_cmds'.");

        string splitCmds = string.Join(",", okCmdsValue.Split(["\r\n", "\n"], StringSplitOptions.None));

        return new ShellPolicyPair(
            new ShellPolicy(SplitSpecs(splitCmds), SplitSet(okDestsValue)),
            new ShellPolicy());
    }

    private static Dictionary<string, List<string>> ParseDefaultSection(string ini)
    {
        Dictionary<string, List<string>> values = new(StringComparer.OrdinalIgnoreCase);
        using StringReader reader = new(ini);

        string? currentSection = null;
        string? currentKey = null;

        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith('#') || trimmed.StartsWith(';'))
            {
                continue;
            }

            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                currentSection = trimmed[1..^1];
                currentKey = null;
                continue;
            }

            if (!string.Equals(currentSection, "DEFAULT", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (char.IsWhiteSpace(line[0]) && currentKey is not null)
            {
                values[currentKey].Add(trimmed);
                continue;
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex < 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();
            currentKey = key;

            if (!values.TryGetValue(key, out List<string>? lines))
            {
                lines = [];
                values[key] = lines;
            }
            else
            {
                lines.Clear();
            }

            lines.Add(value);
        }

        return values;
    }

    private static string? GetJoinedValue(IReadOnlyDictionary<string, List<string>> values, string key)
    {
        return values.TryGetValue(key, out List<string>? lines)
            ? string.Join(Environment.NewLine, lines)
            : null;
    }

    private static IEnumerable<string> SplitSet(string value)
    {
        return value.Split(',')
            .Select(static item => item.Trim())
            .Where(static item => item.Length > 0);
    }

    private static IEnumerable<CmdSpec> SplitSpecs(string value)
    {
        return value.Split(',')
            .Select(static item => item.Trim())
            .Where(static item => item.Length > 0)
            .Select(CmdSpec.FromStr);
    }
}
