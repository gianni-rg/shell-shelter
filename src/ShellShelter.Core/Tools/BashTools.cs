using System.ComponentModel;
using System.Text;
using ShellShelter.Core.Bash;
using ShellShelter.Core.Config;

namespace ShellShelter.Core.Tools;

/// <summary>
/// LLM-callable tools for safely executing bash commands against an allowlist policy.
/// </summary>
/// <remarks>
/// All public methods are designed to be used as MEAI tool functions and are annotated with
/// <see cref="DescriptionAttribute"/> for LLM tool discovery.
/// Each call loads the policy fresh from the user config file (or falls back to built-in defaults),
/// then applies any per-call overrides. Policy state is never shared across calls.
/// </remarks>
public static class BashTools
{
    private static readonly ConfigLoader _configLoader = new();

    /// <summary>
    /// Run a bash shell command line safely and return the combined stdout and stderr.
    /// </summary>
    /// <remarks>
    /// Since the command is run with <c>bash</c>, special characters like <c>$</c> and <c>*</c>
    /// are handled by the shell and must be quoted if literal.
    /// The command is parsed and all calls are checked against an allowlist.
    /// If the command is not allowed, STOP and inform the user of the command run and error
    /// details so they can decide whether to add it to the allowlist or run it themselves.
    /// The default allowlist includes most standard Unix commands and git subcommands that
    /// do not change state or are easily reverted.
    /// All operators are supported. Output redirects are validated against allowed
    /// destinations (default: <c>./</c> and <c>/tmp</c>).
    /// <para><c>rmCmds</c>/<c>rmDests</c> params are comma-separated strings.</para>
    /// </remarks>
    [Description("Run a bash shell command safely against an allowlist. Parse and validate all commands before execution. " +
                 "On policy failure return error details including allowed commands and destinations.")]
    public static async Task<ToolResult> Bash(
        [Description("Bash command string to execute. All shell features (pipes, subshells, redirects) are supported.")] string cmd,
        [Description("Comma-separated command specs to remove from the allowlist for this call only.")] string? rmCmds = null,
        [Description("Comma-separated destination patterns to remove from the allowlist for this call only.")] string? rmDests = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(cmd);

        ShellPolicy policy = LoadBashPolicy().WithRemove(rmCmds, rmDests);
        var maps = BuildExtractionMaps(policy);

        try
        {
            string output = await BashShell.SafeRunAsync(
                cmd,
                policy,
                execFlags: maps.ExecFlags,
                destFlags: maps.DestFlags,
                execPos: maps.ExecPos,
                destPos: maps.DestPos);
            return ToolResult.Success(output);
        }
        catch (DisallowedCmdException ex)
        {
            return ToolResult.Denied(ex.Message, policy.OkCmds, policy.OkDests);
        }
        catch (DisallowedDestException ex)
        {
            return ToolResult.Denied(ex.Message, policy.OkCmds, policy.OkDests);
        }
        catch (Exception ex)
        {
            return ToolResult.ExecutionFailed(ex.Message);
        }
    }

    /// <summary>
    /// Run a bash shell command safely with explicit or modified policy overrides.
    /// </summary>
    /// <remarks>
    /// Use this function only with explicit user permission when the default allowlist is
    /// insufficient. The <c>cmds</c> and <c>dests</c> parameters replace the entire policy;
    /// <c>addCmds</c>/<c>addDests</c> extend the effective policy; <c>rmCmds</c>/<c>rmDests</c>
    /// shrink it. All comma-separated string params use the same format as the config file.
    /// </remarks>
    [Description("Run a bash shell command with explicit allowlist overrides. " +
                 "DO NOT USE without prior user permission for the specific override being applied.")]
    public static async Task<ToolResult> UnsafeBash(
        [Description("Bash command string to execute.")] string cmd,
        [Description("Full replacement command allowlist (comma-separated). Defaults to ok_cmds when null.")] string? cmds = null,
        [Description("Full replacement destination allowlist (comma-separated). Defaults to ok_dests when null.")] string? dests = null,
        [Description("Comma-separated command specs to add to the effective allowlist.")] string? addCmds = null,
        [Description("Comma-separated destination patterns to add to the effective allowlist.")] string? addDests = null,
        [Description("Comma-separated command specs to remove from the effective allowlist.")] string? rmCmds = null,
        [Description("Comma-separated destination patterns to remove from the effective allowlist.")] string? rmDests = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(cmd);

        ShellPolicy basePolicy = cmds is not null || dests is not null
            ? BuildOverridePolicy(cmds, dests)
            : LoadBashPolicy();

        ShellPolicy policy = basePolicy.WithAdd(addCmds, addDests).WithRemove(rmCmds, rmDests);
        var maps = BuildExtractionMaps(policy);

        try
        {
            string output = await BashShell.SafeRunAsync(
                cmd,
                policy,
                execFlags: maps.ExecFlags,
                destFlags: maps.DestFlags,
                execPos: maps.ExecPos,
                destPos: maps.DestPos);
            return ToolResult.Success(output);
        }
        catch (DisallowedCmdException ex)
        {
            return ToolResult.Denied(ex.Message, policy.OkCmds, policy.OkDests);
        }
        catch (DisallowedDestException ex)
        {
            return ToolResult.Denied(ex.Message, policy.OkCmds, policy.OkDests);
        }
        catch (Exception ex)
        {
            return ToolResult.ExecutionFailed(ex.Message);
        }
    }

    /// <summary>
    /// Run <c>ex</c> commands on a file via bash.
    /// </summary>
    /// <remarks>
    /// Always runs in <c>noai</c> and <c>et</c> mode.
    /// <c>x</c> is always added at the end — do not add <c>wq</c> or <c>x</c> to <c>cmds</c>.
    /// Use <c>Ex(path, linenums: true)</c> with no cmds to get an initial file listing with line numbers.
    /// Can also create new files (use <c>a</c> with a non-existent path).
    /// Always end <c>a</c>/<c>i</c> blocks with a lone <c>.</c> on its own line.
    /// This is a Unix-only tool (requires <c>ex</c> on PATH).
    /// </remarks>
    [Description("Run ex (vi) commands on a file via bash. Unix-only. Always appends 'x' to save. " +
                 "Set linenums=true to get a line-numbered listing without editing.")]
    public static async Task<ToolResult> Ex(
        [Description("Path to the file to operate on.")] string path,
        [Description("ex commands to run. Embedded newlines are handled via heredoc.")] string cmds = "",
        [Description("Shiftwidth for indent/dedent commands.")] int shiftwidth = 4,
        [Description("When true, return file listing with line numbers as the final output.")] bool lineNumbers = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        ShellPolicy policy = LoadBashPolicy();
        if (!ShellValidator.ValidateDestination(path, policy.OkDests))
        {
            var denyEx = new DisallowedDestException(path);
            return ToolResult.Denied(denyEx.Message, policy.OkCmds, policy.OkDests);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");

        string trimmedCmds = cmds.Trim();
        if (lineNumbers)
            trimmedCmds = (trimmedCmds.Length > 0 ? trimmedCmds + "\n" : "") + "%#";
        if (trimmedCmds.Length > 0)
            trimmedCmds += "\n";

        // Build the heredoc bash command — path is shell-quoted via QuoteArgument to prevent injection
        string quotedPath = QuoteArgument(path);
        string bashCmd = $"""
            ex --clean -V1 {quotedPath} <<'EX_EOF'
            set noai
            set et
            set sw={shiftwidth}
            {trimmedCmds}x
            EX_EOF
            """;

        try
        {
            var result = await BashShell.RunCapturedAsync(bashCmd, policy);
            // Filter ex's verbose output: keep only E### errors (exclude E749 cursor quirk)
            string[] errLines = result.StdErr.Split("Entering Ex mode.")
                [^1].Split('\n', StringSplitOptions.RemoveEmptyEntries);
            string errors = string.Join('\n',
                errLines.Where(l => l.Length > 1 && l[0] == 'E' && char.IsDigit(l[1]) && !l.Contains("E749")));

            if (errors.Length > 0)
                return ToolResult.ExecutionFailed(errors);

            string output = result.StdOut.Trim().Length > 0 ? result.StdOut : $"Applied to {path}";
            return ToolResult.Success(output);
        }
        catch (DisallowedCmdException ex)
        {
            return ToolResult.Denied(ex.Message, policy.OkCmds, policy.OkDests);
        }
        catch (DisallowedDestException ex)
        {
            return ToolResult.Denied(ex.Message, policy.OkCmds, policy.OkDests);
        }
        catch (Exception ex)
        {
            return ToolResult.ExecutionFailed(ex.Message);
        }
    }

    /// <summary>
    /// Run <c>ex</c> commands on a string and return the modified string.
    /// </summary>
    /// <remarks>
    /// Writes the content to a temporary file, runs <see cref="Ex"/>, reads the result back.
    /// Use <c>ExStr(s, linenums: true)</c> with no cmds to get a line-numbered listing.
    /// This is a Unix-only tool.
    /// </remarks>
    [Description("Run ex (vi) commands on a string and return the modified string. Unix-only.")]
    public static async Task<ToolResult> ExStr(
        [Description("String content to operate on.")] string content,
        [Description("ex commands to run.")] string cmds = "",
        [Description("Shiftwidth for indent/dedent commands.")] int shiftwidth = 4,
        [Description("Include line numbers in the response.")] bool lineNumbers = false)
    {
        ArgumentNullException.ThrowIfNull(content);

        string tmpPath = Path.GetTempFileName() + ".txt";
        try
        {
            await File.WriteAllTextAsync(tmpPath, content);

            if (lineNumbers)
                return await Ex(tmpPath, cmds: "", shiftwidth: shiftwidth, lineNumbers: true);

            ToolResult result = await Ex(tmpPath, cmds, shiftwidth: shiftwidth);
            if (!result.IsSuccess)
                return result;

            string modified = await File.ReadAllTextAsync(tmpPath);
            return ToolResult.Success(modified);
        }
        finally
        {
            if (File.Exists(tmpPath))
                File.Delete(tmpPath);
        }
    }

    /// <summary>
    /// Run the <c>sed</c> command against a file.
    /// </summary>
    /// <remarks>
    /// When <c>inplace</c> is true the file path must match an allowed destination.
    /// This is a Unix-only tool.
    /// </remarks>
    [Description("Run sed against a file. Set inplace=true to edit in place (dest must be in allowed destinations). Unix-only.")]
    public static async Task<ToolResult> Sed(
        [Description("Path to the file.")] string path,
        [Description("sed arguments (e.g. 's/x/y/' or '1,$p').")] string sedCmds,
        [Description("Edit the file in place (sed -i).")] bool inplace = false,
        [Description("Suppress default output (sed -n).")] bool quiet = false,
        [Description("Append line numbers to output after command.")] bool lineNumbers = false)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(sedCmds);
        if (lineNumbers && quiet)
            throw new ArgumentException("lineNumbers and quiet cannot be used together.");

        ShellPolicy policy = LoadBashPolicy();

        // Validate the destination before building the command when inplace
        if (inplace)
        {
            bool destAllowed = ShellValidator.ValidateDestination(path, policy.OkDests);
            if (!destAllowed)
            {
                var denyEx = new DisallowedDestException(path);
                return ToolResult.Denied(denyEx.Message, policy.OkCmds, policy.OkDests);
            }
        }

        var flags = new StringBuilder();
        if (inplace)
        {
            if (OperatingSystem.IsMacOS())
                flags.Append("-i '' ");
            else
                flags.Append("-i ");
        }
        if (quiet)
            flags.Append("-n ");

        string quotedCmds = QuoteArgument(sedCmds);
        string quotedPath = QuoteArgument(path);
        // Add sed to the allowlist temporarily so the validator recognises it
        ShellPolicy sedPolicy = policy.WithAdd(addCmds: "sed");
        string bashCmd = $"sed {flags}{quotedCmds} {quotedPath}";

        try
        {
            string output = await BashShell.SafeRunAsync(bashCmd, sedPolicy);

            if (lineNumbers)
            {
                string[] lines = inplace
                    ? await File.ReadAllLinesAsync(path)
                    : output.Split('\n');
                int width = lines.Length.ToString().Length;
                output = string.Join('\n',
                    lines.Select((l, i) => $"{(i + 1).ToString().PadLeft(width)} {l}"));
            }

            return ToolResult.Success(output);
        }
        catch (DisallowedCmdException ex)
        {
            return ToolResult.Denied(ex.Message, sedPolicy.OkCmds, sedPolicy.OkDests);
        }
        catch (DisallowedDestException ex)
        {
            return ToolResult.Denied(ex.Message, sedPolicy.OkCmds, sedPolicy.OkDests);
        }
        catch (Exception ex)
        {
            return ToolResult.ExecutionFailed(ex.Message);
        }
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static ShellPolicy LoadBashPolicy()
    {
        string configPath = ConfigLoader.GetDefaultConfigPath();
        if (File.Exists(configPath))
        {
            try
            {
                return _configLoader.Load(configPath).BashPolicy;
            }
            catch
            {
                // Fall back to built-in defaults if the config is malformed
            }
        }

        return _configLoader.LoadFromText(DefaultConfigs.BashDefaultIni).BashPolicy;
    }

    private static ShellPolicy BuildOverridePolicy(string? cmds, string? dests)
    {
        var okCmds = new HashSet<CmdSpec>();
        var okDests = new HashSet<string>();

        if (cmds is not null)
        {
            foreach (string spec in cmds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                okCmds.Add(CmdSpec.FromStr(spec));
        }

        if (dests is not null)
        {
            foreach (string dest in dests.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                okDests.Add(dest);
        }

        return new ShellPolicy(okCmds, okDests);
    }

    private static (
        IReadOnlyDictionary<string, IReadOnlySet<string>> ExecFlags,
        IReadOnlyDictionary<string, IReadOnlySet<string>> DestFlags,
        IReadOnlyDictionary<string, IReadOnlySet<int>> ExecPos,
        IReadOnlyDictionary<string, IReadOnlySet<int>> DestPos)
        BuildExtractionMaps(ShellPolicy policy)
    {
        var execFlags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var destFlags = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var execPos = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        var destPos = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        foreach (CmdSpec spec in policy.OkCmds)
        {
            if (spec.Name.Count == 0)
                continue;

            string cmdName = spec.Name[0];

            if (spec.ExecFlags.Count > 0)
            {
                if (!execFlags.TryGetValue(cmdName, out var values))
                {
                    values = new HashSet<string>(StringComparer.Ordinal);
                    execFlags[cmdName] = values;
                }

                values.UnionWith(spec.ExecFlags);
            }

            if (spec.DestFlags.Count > 0)
            {
                if (!destFlags.TryGetValue(cmdName, out var values))
                {
                    values = new HashSet<string>(StringComparer.Ordinal);
                    destFlags[cmdName] = values;
                }

                values.UnionWith(spec.DestFlags);
            }

            if (spec.ExecPos.Count > 0)
            {
                if (!execPos.TryGetValue(cmdName, out var values))
                {
                    values = new HashSet<int>();
                    execPos[cmdName] = values;
                }

                values.UnionWith(spec.ExecPos);
            }

            if (spec.DestPos.Count > 0)
            {
                if (!destPos.TryGetValue(cmdName, out var values))
                {
                    values = new HashSet<int>();
                    destPos[cmdName] = values;
                }

                values.UnionWith(spec.DestPos);
            }
        }

        return (
            execFlags.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlySet<string>)kvp.Value,
                StringComparer.Ordinal),
            destFlags.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlySet<string>)kvp.Value,
                StringComparer.Ordinal),
            execPos.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlySet<int>)kvp.Value,
                StringComparer.Ordinal),
            destPos.ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlySet<int>)kvp.Value,
                StringComparer.Ordinal));
    }

    /// <summary>
    /// Produces a POSIX single-quote-escaped shell argument.
    /// Single quotes cannot appear inside single-quoted strings in POSIX sh;
    /// the standard workaround is to end the quote, insert a literal <c>'</c>, and reopen.
    /// </summary>
    private static string QuoteArgument(string arg) =>
        "'" + arg.Replace("'", "'\\''") + "'";
}
