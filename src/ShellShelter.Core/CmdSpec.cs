namespace ShellShelter.Core;

/// <summary>
/// Represents an allowlisted command prefix with optional denied flags and nested command or destination metadata.
/// </summary>
public sealed class CmdSpec : IEquatable<CmdSpec>
{
    private readonly string[] _name;
    private readonly HashSet<string> _denied;
    private readonly HashSet<string> _execFlags;
    private readonly HashSet<string> _destFlags;
    private readonly HashSet<int> _execPos;
    private readonly HashSet<int> _destPos;

    /// <summary>
    /// Initializes a new instance of the <see cref="CmdSpec"/> class.
    /// </summary>
    /// <param name="name">The command name prefix. Whitespace is split using Python-like <c>str.split()</c> semantics.</param>
    /// <param name="denied">Flags that cause the command to be rejected.</param>
    /// <param name="execFlags">Flags whose next argument is itself a command.</param>
    /// <param name="destFlags">Flags whose next argument is a destination path.</param>
    /// <param name="execPos">Positional argument indices whose values are commands.</param>
    /// <param name="destPos">Positional argument indices whose values are destinations.</param>
    public CmdSpec(
        string name,
        IEnumerable<string>? denied = null,
        IEnumerable<string>? execFlags = null,
        IEnumerable<string>? destFlags = null,
        IEnumerable<int>? execPos = null,
        IEnumerable<int>? destPos = null)
    {
        ArgumentNullException.ThrowIfNull(name);

        _name = SplitName(name);
        _denied = CreateStringSet(denied);
        _execFlags = CreateStringSet(execFlags);
        _destFlags = CreateStringSet(destFlags);
        _execPos = CreateIntSet(execPos);
        _destPos = CreateIntSet(destPos);
    }

    /// <summary>
    /// Gets the command name prefix tokens.
    /// </summary>
    public IReadOnlyList<string> Name => _name;

    /// <summary>
    /// Gets the denied flags.
    /// </summary>
    public IReadOnlySet<string> Denied => _denied;

    /// <summary>
    /// Gets flags whose next argument is a command.
    /// </summary>
    public IReadOnlySet<string> ExecFlags => _execFlags;

    /// <summary>
    /// Gets flags whose next argument is a destination.
    /// </summary>
    public IReadOnlySet<string> DestFlags => _destFlags;

    /// <summary>
    /// Gets positional argument indices whose values are commands.
    /// </summary>
    public IReadOnlySet<int> ExecPos => _execPos;

    /// <summary>
    /// Gets positional argument indices whose values are destinations.
    /// </summary>
    public IReadOnlySet<int> DestPos => _destPos;

    /// <summary>
    /// Parses a serialized command specification in Python safecmd format.
    /// </summary>
    /// <param name="value">The serialized command specification.</param>
    /// <returns>The parsed command specification.</returns>
    public static CmdSpec FromStr(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        string[] parts = value.Split(':');
        string name = parts[0];
        List<string>? denied = null;
        List<string>? execFlags = null;
        List<int>? execPos = null;
        List<string>? destFlags = null;
        List<int>? destPos = null;

        foreach (string part in parts.Skip(1))
        {
            if (part.StartsWith("exec=", StringComparison.Ordinal))
            {
                (execFlags, execPos) = SplitPos(part[5..].Split('|'));
            }
            else if (part.StartsWith("dest=", StringComparison.Ordinal))
            {
                (destFlags, destPos) = SplitPos(part[5..].Split('|'));
            }
            else
            {
                denied = part.Length == 0 ? null : [.. part.Split('|')];
            }
        }

        return new CmdSpec(name, denied, execFlags, destFlags, execPos, destPos);
    }

    /// <summary>
    /// Returns <see langword="true"/> when the supplied token list matches this specification and does not contain denied flags.
    /// </summary>
    /// <param name="tokens">The tokenized command.</param>
    /// <returns><see langword="true"/> if the tokens are allowed; otherwise <see langword="false"/>.</returns>
    public bool IsAllowed(IReadOnlyList<string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        if (tokens.Count < _name.Length)
        {
            return false;
        }

        for (int index = 0; index < _name.Length; index++)
        {
            if (!string.Equals(tokens[index], _name[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        if (_denied.Count == 0)
        {
            return true;
        }

        foreach (string denied in _denied)
        {
            if (tokens.Contains(denied, StringComparer.Ordinal))
            {
                return false;
            }

            if (denied.StartsWith("--", StringComparison.Ordinal))
            {
                string longFlagPrefix = denied + "=";
                if (tokens.Any(token => token.StartsWith(longFlagPrefix, StringComparison.Ordinal)))
                {
                    return false;
                }
            }
            else if (denied.Length == 2 && denied[0] == '-')
            {
                char shortFlag = denied[1];
                foreach (string token in tokens)
                {
                    if (token.StartsWith("-", StringComparison.Ordinal)
                        && !token.StartsWith("--", StringComparison.Ordinal)
                        && token.IndexOf(shortFlag, 1) >= 1)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <inheritdoc />
    public bool Equals(CmdSpec? other)
    {
        return other is not null && _name.SequenceEqual(other._name, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        return obj is CmdSpec other && Equals(other);
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        HashCode hashCode = new();
        foreach (string part in _name)
        {
            hashCode.Add(part, StringComparer.Ordinal);
        }

        return hashCode.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString()
    {
        List<string> parts = [string.Join(" ", _name)];

        if (_denied.Count > 0)
        {
            parts.Add($"!{FormatSet(_denied.OrderBy(static value => value, StringComparer.Ordinal))}");
        }

        if (_execFlags.Count > 0)
        {
            parts.Add($"exec={FormatSet(_execFlags.OrderBy(static value => value, StringComparer.Ordinal))}");
        }

        if (_execPos.Count > 0)
        {
            parts.Add($"exec_pos={FormatSet(_execPos.OrderBy(static value => value))}");
        }

        if (_destFlags.Count > 0)
        {
            parts.Add($"dest={FormatSet(_destFlags.OrderBy(static value => value, StringComparer.Ordinal))}");
        }

        if (_destPos.Count > 0)
        {
            parts.Add($"dest_pos={FormatSet(_destPos.OrderBy(static value => value))}");
        }

        return string.Join(" ", parts);
    }

    private static string[] SplitName(string name)
    {
        return name.Split(
            [' ', '\t', '\r', '\n', '\f', '\v'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static HashSet<string> CreateStringSet(IEnumerable<string>? values)
    {
        return values is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(values, StringComparer.Ordinal);
    }

    private static HashSet<int> CreateIntSet(IEnumerable<int>? values)
    {
        return values is null ? [] : [.. values];
    }

    private static (List<string> Flags, List<int> Positions) SplitPos(IEnumerable<string> values)
    {
        List<string> flags = [];
        List<int> positions = [];

        foreach (string value in values)
        {
            if (value.StartsWith('$'))
            {
                positions.Add(int.Parse(value[1..], System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                flags.Add(value);
            }
        }

        return (flags, positions);
    }

    private static string FormatSet<T>(IEnumerable<T> values)
    {
        return $"{{{string.Join(", ", values)}}}";
    }
}
