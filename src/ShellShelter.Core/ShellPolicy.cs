namespace ShellShelter.Core;

/// <summary>
/// Represents the allowed command specifications and destination prefixes for a shell.
/// </summary>
public sealed class ShellPolicy
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ShellPolicy"/> class.
    /// </summary>
    /// <param name="okCmds">The allowed command specs.</param>
    /// <param name="okDests">The allowed destination prefixes.</param>
    public ShellPolicy(IEnumerable<CmdSpec>? okCmds = null, IEnumerable<string>? okDests = null)
    {
        OkCmds = CreateCommandSet(okCmds);
        OkDests = CreateDestinationSet(okDests);
    }

    /// <summary>
    /// Gets the allowed command specs.
    /// </summary>
    public HashSet<CmdSpec> OkCmds { get; }

    /// <summary>
    /// Gets the allowed destination prefixes.
    /// </summary>
    public HashSet<string> OkDests { get; }

    /// <summary>
    /// Creates a cloned policy with additional command specs and destination prefixes.
    /// </summary>
    /// <param name="addCmds">Command specs to add.</param>
    /// <param name="addDests">Destination prefixes to add.</param>
    /// <returns>A cloned policy containing the requested additions.</returns>
    public ShellPolicy WithAdd(IEnumerable<CmdSpec>? addCmds = null, IEnumerable<string>? addDests = null)
    {
        ShellPolicy clone = Clone();
        AddCommandValues(clone.OkCmds, addCmds);
        AddDestinationValues(clone.OkDests, addDests);
        return clone;
    }

    /// <summary>
    /// Creates a cloned policy with additional commands and destinations parsed from comma-separated strings.
    /// </summary>
    /// <param name="addCmds">Comma-separated command spec strings to add (e.g. <c>"sed, ex:dest=$0"</c>).</param>
    /// <param name="addDests">Comma-separated destination prefix strings to add (e.g. <c>"/tmp, ./"</c>).</param>
    /// <returns>A cloned policy with the parsed specs added.</returns>
    public ShellPolicy WithAdd(string? addCmds, string? addDests = null) =>
        WithAdd(ParseCmds(addCmds), ParseDests(addDests));

    /// <summary>
    /// Creates a cloned policy with command specs and destination prefixes removed.
    /// </summary>
    /// <param name="removeCmds">Command specs to remove.</param>
    /// <param name="removeDests">Destination prefixes to remove.</param>
    /// <returns>A cloned policy containing the requested removals.</returns>
    public ShellPolicy WithRemove(IEnumerable<CmdSpec>? removeCmds = null, IEnumerable<string>? removeDests = null)
    {
        ShellPolicy clone = Clone();
        RemoveCommandValues(clone.OkCmds, removeCmds);
        RemoveDestinationValues(clone.OkDests, removeDests);
        return clone;
    }

    /// <summary>
    /// Creates a cloned policy with commands and destinations parsed from comma-separated strings removed.
    /// </summary>
    /// <param name="removeCmds">Comma-separated command names to remove (e.g. <c>"rm, mv"</c>).</param>
    /// <param name="removeDests">Comma-separated destination prefixes to remove (e.g. <c>"/tmp"</c>).</param>
    /// <returns>A cloned policy with the parsed entries removed.</returns>
    public ShellPolicy WithRemove(string? removeCmds, string? removeDests = null) =>
        WithRemove(ParseCmds(removeCmds), ParseDests(removeDests));

    private static IEnumerable<CmdSpec>? ParseCmds(string? csv) =>
        csv is null ? null :
            csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Select(CmdSpec.FromStr);

    private static IEnumerable<string>? ParseDests(string? csv) =>
        csv is null ? null :
            csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private ShellPolicy Clone()
    {
        return new ShellPolicy(OkCmds, OkDests);
    }

    private static HashSet<CmdSpec> CreateCommandSet(IEnumerable<CmdSpec>? values)
    {
        HashSet<CmdSpec> result = [];
        AddCommandValues(result, values);
        return result;
    }

    private static HashSet<string> CreateDestinationSet(IEnumerable<string>? values)
    {
        HashSet<string> result = new(StringComparer.Ordinal);
        AddDestinationValues(result, values);
        return result;
    }

    private static void AddCommandValues(HashSet<CmdSpec> target, IEnumerable<CmdSpec>? values)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (values is null)
        {
            return;
        }

        foreach (CmdSpec? value in values)
        {
            if (value is null)
            {
                continue;
            }

            if (target.TryGetValue(value, out CmdSpec? existing))
            {
                if (!existing.HasSameSemantics(value))
                {
                    CmdSpec merged = existing.MergeWith(value);
                    target.Remove(existing);
                    target.Add(merged);
                }

                continue;
            }

            target.Add(value);
        }
    }

    private static void AddDestinationValues(HashSet<string> target, IEnumerable<string>? values)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (values is null)
        {
            return;
        }

        foreach (string? value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            target.Add(value.Trim());
        }
    }

    private static void RemoveCommandValues(HashSet<CmdSpec> target, IEnumerable<CmdSpec>? values)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (values is null)
        {
            return;
        }

        foreach (CmdSpec? value in values)
        {
            if (value is null)
            {
                continue;
            }

            target.Remove(value);
        }
    }

    private static void RemoveDestinationValues(HashSet<string> target, IEnumerable<string>? values)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (values is null)
        {
            return;
        }

        foreach (string? value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            target.Remove(value.Trim());
        }
    }
}
