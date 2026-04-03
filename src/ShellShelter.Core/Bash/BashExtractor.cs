using System.Diagnostics;
using System.Text.Json;

namespace ShellShelter.Core.Bash;

/// <summary>
/// Extracts commands from bash command strings using the shfmt AST parser.
/// </summary>
/// <remarks>
/// This class uses shfmt to parse bash syntax into a JSON AST, then walks the AST to extract
/// all executable commands, operators, and destination redirects. It handles recursive
/// command extraction for nested commands via exec flags and positions.
/// </remarks>
public sealed class BashExtractor : IShellExtractor
{
    private const string ShfmtDefault = "shfmt";
    private const string HandledTypesSetJson = """
        ["File","CallExpr","BinaryCmd","Subshell","Block","IfClause","WhileClause","ForClause","CaseClause","CaseItem","FuncDecl","DeclClause","Lit","SglQuoted","DblQuoted","ParamExp","CmdSubst","ProcSubst","Hdoc","Word","WordIter","Redirect","Comment","ArithmExp","ArithmCmd"]
        """;

    private static readonly HashSet<string> HandledTypes = new(
        JsonDocument.Parse(HandledTypesSetJson)
            .RootElement.EnumerateArray()
            .Select(e => e.GetString()!)
    );

    /// <summary>
    /// Operator codes used by shfmt and their string representations.
    /// </summary>
    private static readonly Dictionary<int, string> OpMap = new()
    {
        // shfmt v3.13+ op codes
        { 11, "&&" },
        { 12, "||" },
        { 13, "|" },
        { 14, "|&" },
        { 63, ">" },
        { 64, ">>" },
        { 65, "<" },
        { 67, "<&" },
        { 68, ">&" },
        { 74, "&>" },
        { 76, "&>>" }
    };

    /// <summary>
    /// Write redirect operator codes (subset of OpMap).
    /// </summary>
    private static readonly Dictionary<int, string> WriteOps = new()
    {
        { 63, ">" },
        { 64, ">>" },
        { 74, "&>" },
        { 76, "&>>" }
    };

    /// <summary>
    /// Parses a bash command string using shfmt and returns the JSON AST.
    /// </summary>
    /// <param name="cmd">The bash command to parse.</param>
    /// <param name="shfmtPath">Path to the shfmt executable. Defaults to "shfmt".</param>
    /// <returns>The parsed JSON AST as a JsonElement.</returns>
    /// <exception cref="FileNotFoundException">Thrown when shfmt is not found in PATH.</exception>
    /// <exception cref="InvalidOperationException">Thrown when shfmt parsing fails.</exception>
    public static async Task<JsonElement> ParseBashAsync(string cmd, string shfmtPath = ShfmtDefault)
    {
        if (string.IsNullOrEmpty(cmd))
            throw new ArgumentNullException(nameof(cmd));

        ToolAvailabilityBootstrap.EnsureShfmtAvailable(shfmtPath);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = shfmtPath,
                Arguments = "--to-json",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        // Write command to stdin and close it
        await process.StandardInput.WriteAsync(cmd);
        process.StandardInput.Close();

        // Read output and error
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"shfmt failed: {error}");

        return JsonDocument.Parse(output).RootElement;
    }

    /// <summary>
    /// Extracts text from a single word part node in the shfmt AST.
    /// </summary>
    private static string PartText(JsonElement part, string originalCmd)
    {
        if (part.ValueKind != JsonValueKind.Object)
            return string.Empty;

        string? type = part.GetPropertyOrNull("Type")?.GetString();

        return type switch
        {
            "Lit" => part.GetPropertyOrNull("Value")?.GetString()?.Replace("\\ ", " ") ?? string.Empty,
            "SglQuoted" => part.GetPropertyOrNull("Value")?.GetString() ?? string.Empty,
            "DblQuoted" or "Hdoc" => AggregatePartText(
                part.GetPropertyOrNull("Parts")?.EnumerateArrayOrEmpty() ?? Enumerable.Empty<JsonElement>(),
                originalCmd),
            "ParamExp" => ConstructParamExp(part, originalCmd),
            "CmdSubst" or "ProcSubst" => ExtractSubstitutionText(part, originalCmd),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Constructs a parameter expansion string (e.g., ${VAR}, $VAR[0]).
    /// </summary>
    private static string ConstructParamExp(JsonElement part, string originalCmd)
    {
        if (!part.TryGetProperty("Param", out var paramEl) || paramEl.ValueKind != JsonValueKind.Object)
            return string.Empty;

        string? name = paramEl.GetPropertyOrNull("Value")?.GetString();
        if (string.IsNullOrEmpty(name))
            return string.Empty;

        // Handle array indexing
        if (part.GetPropertyOrNull("Index") is JsonElement indexEl)
        {
            string index = WordText(indexEl, originalCmd);
            name += $"[{index}]";
        }

        // Check if it's braced
        bool isBraced = part.GetPropertyOrNull("Rbrace") is JsonElement;
        return isBraced ? "${" + name + "}" : "$" + name;
    }

    /// <summary>
    /// Extracts command substitution or process substitution text.
    /// </summary>
    private static string ExtractSubstitutionText(JsonElement part, string originalCmd)
    {
        if (!part.TryGetProperty("Pos", out var posEl) || !part.TryGetProperty("End", out var endEl))
            return string.Empty;

        int start = posEl.GetPropertyOrNull("Offset")?.GetInt32() ?? 0;
        int end = endEl.GetPropertyOrNull("Offset")?.GetInt32() ?? 0;

        if (start >= 0 && end <= originalCmd.Length && start < end)
            return originalCmd[start..end];

        return string.Empty;
    }

    /// <summary>
    /// Aggregates part text by concatenating text from all parts.
    /// </summary>
    private static string AggregatePartText(IEnumerable<JsonElement> parts, string originalCmd)
    {
        return string.Concat(parts.Select(p => PartText(p, originalCmd)));
    }

    /// <summary>
    /// Converts a Word node into its full text representation.
    /// </summary>
    private static string WordText(JsonElement? word, string originalCmd)
    {
        if (word is null || word.Value.ValueKind != JsonValueKind.Object)
            return string.Empty;

        var w = word.Value;
        var parts = w.GetPropertyOrNull("Parts")?.EnumerateArrayOrEmpty() ?? Enumerable.Empty<JsonElement>();
        return string.Concat(parts.Select(p => PartText(p, originalCmd)));
    }

    /// <summary>
    /// Recursively visits statement nodes and collects commands.
    /// </summary>
    private static void VisitStmts(
        IEnumerable<JsonElement> stmts,
        string originalCmd,
        List<List<string>> commands)
    {
        foreach (var stmt in stmts.Where(s => s.ValueKind == JsonValueKind.Object))
        {
            VisitNode(stmt, originalCmd, commands);
            
            // Handle redirects attached to this statement
            var redirects = stmt.GetPropertyOrNull("Redirs")?.EnumerateArrayOrEmpty() ?? Enumerable.Empty<JsonElement>();
            foreach (var redirect in redirects.Where(r => r.ValueKind == JsonValueKind.Object))
            {
                if (commands.Count == 0)
                    continue;

                // Handle heredoc
                if (redirect.GetPropertyOrNull("Hdoc") is JsonElement hdoc && hdoc.ValueKind == JsonValueKind.Object)
                {
                    string hdocText = WordText(hdoc, originalCmd).TrimEnd('\n');
                    commands[^1].Add(hdocText);
                }
                // Handle here-string (<<<)
                else if (redirect.GetPropertyOrNull("Op")?.GetInt32() == 73)
                {
                    commands[^1].Add("<<<");
                    if (redirect.GetPropertyOrNull("Word") is JsonElement word && word.ValueKind == JsonValueKind.Object)
                        commands[^1].Add(WordText(word, originalCmd));
                }
            }
        }
    }

    /// <summary>
    /// Recursively visits a single node.
    /// </summary>
    private static void VisitNode(JsonElement node, string originalCmd, List<List<string>> commands)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        // Collect command arguments if this is a CallExpr
        if (node.GetPropertyOrNull("Args") is JsonElement argsEl && argsEl.ValueKind == JsonValueKind.Array)
        {
            var args = argsEl.EnumerateArray().ToList();
            var cmdTokens = args.Select(a => WordText(a, originalCmd)).ToList();
            commands.Add(cmdTokens);

            // Visit nested statements in argument parts
            foreach (var arg in args.Where(a => a.ValueKind == JsonValueKind.Object))
            {
                var nestedStmts = CollectNestedStmts(arg);
                foreach (var stmtList in nestedStmts)
                {
                    VisitStmts(stmtList, originalCmd, commands);
                }
            }
        }

        // Visit single-node properties
        foreach (var key in new[] { "Cmd", "X", "Y", "Cond", "Loop" })
        {
            if (node.GetPropertyOrNull(key) is JsonElement child && child.ValueKind == JsonValueKind.Object)
                VisitNode(child, originalCmd, commands);
        }

        // Visit statement lists
        foreach (var key in new[] { "Stmts", "Items", "Cases", "Then", "Else", "Do" })
        {
            if (node.GetPropertyOrNull(key) is JsonElement stmtList && stmtList.ValueKind == JsonValueKind.Array)
                VisitStmts(stmtList.EnumerateArray(), originalCmd, commands);
        }
    }

    /// <summary>
    /// Recursively collects all Stmts lists from nested Parts.
    /// </summary>
    private static IEnumerable<IEnumerable<JsonElement>> CollectNestedStmts(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            yield break;

        // Check for Stmts in this element
        if (element.GetPropertyOrNull("Stmts") is JsonElement stmts && stmts.ValueKind == JsonValueKind.Array)
            yield return stmts.EnumerateArray();

        // Recursively check Parts
        if (element.GetPropertyOrNull("Parts") is JsonElement parts && parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                foreach (var nested in CollectNestedStmts(part))
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// Walks the AST and collects all operators into a set.
    /// </summary>
    private static HashSet<string> CollectOps(JsonElement node)
    {
        var ops = new HashSet<string>();
        CollectOpsRecursive(node, ops);
        return ops;
    }

    /// <summary>
    /// Recursively collects operators.
    /// </summary>
    private static void CollectOpsRecursive(JsonElement node, HashSet<string> ops)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        // Check attribute-based operators. Properties must exist and be meaningful.
        if (node.GetPropertyOrNull("Background") is JsonElement bgEl
            && bgEl.ValueKind == JsonValueKind.True)
            ops.Add("&");

        if (node.GetPropertyOrNull("Semicolon") is JsonElement)
            ops.Add(";");

        if (node.GetPropertyOrNull("Assigns") is JsonElement assignsEl
            && assignsEl.ValueKind == JsonValueKind.Array
            && assignsEl.GetArrayLength() > 0)
            ops.Add("=");

        // Check Op field
        if (node.GetPropertyOrNull("Op")?.GetInt32() is int opCode && OpMap.TryGetValue(opCode, out var opStr))
            ops.Add(opStr);

        // Recurse into all properties
        foreach (var prop in node.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
                CollectOpsRecursive(prop.Value, ops);
            else if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.Value.EnumerateArray())
                    CollectOpsRecursive(item, ops);
            }
        }
    }

    /// <summary>
    /// Walks the AST and collects all write redirect destinations.
    /// </summary>
    private static List<(string Op, string Dest)> CollectRedirects(JsonElement node, string originalCmd)
    {
        var redirects = new List<(string, string)>();
        CollectRedirectsRecursive(node, originalCmd, redirects);
        return redirects;
    }

    /// <summary>
    /// Recursively collects redirects.
    /// </summary>
    private static void CollectRedirectsRecursive(JsonElement node, string originalCmd, List<(string, string)> redirects)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        // Check Redirs array
        if (node.GetPropertyOrNull("Redirs") is JsonElement redirList && redirList.ValueKind == JsonValueKind.Array)
        {
            foreach (var redir in redirList.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object))
            {
                int? opCode = redir.GetPropertyOrNull("Op")?.GetInt32();
                if (opCode.HasValue && WriteOps.TryGetValue(opCode.Value, out var op) &&
                    redir.GetPropertyOrNull("Word") is JsonElement word && word.ValueKind == JsonValueKind.Object)
                {
                    redirects.Add((op, WordText(word, originalCmd)));
                }
            }
        }

        // Recurse into all properties
        foreach (var prop in node.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
                CollectRedirectsRecursive(prop.Value, originalCmd, redirects);
            else if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.Value.EnumerateArray())
                    CollectRedirectsRecursive(item, originalCmd, redirects);
            }
        }
    }

    /// <summary>
    /// Scans commands for executable and destination flags and positional arguments.
    /// </summary>
    private static (List<string> ExtraCommands, List<(string, string)> ExtraDests) ScanFlagArgs(
        IEnumerable<IReadOnlyList<string>> commands,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? execFlags = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? destFlags = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? destPos = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? execPos = null)
    {
        var extraCommands = new List<string>();
        var extraDests = new List<(string, string)>();

        execFlags ??= new Dictionary<string, IReadOnlySet<string>>();
        destFlags ??= new Dictionary<string, IReadOnlySet<string>>();
        destPos ??= new Dictionary<string, IReadOnlySet<int>>();
        execPos ??= new Dictionary<string, IReadOnlySet<int>>();

        foreach (var tokens in commands)
        {
            if (tokens.Count == 0)
                continue;

            string cmdName = tokens[0];

            // Scan for exec flags
            if (execFlags.TryGetValue(cmdName, out var execFlagSet))
            {
                for (int i = 0; i < tokens.Count - 1; i++)
                {
                    if (execFlagSet.Contains(tokens[i]))
                        extraCommands.Add(tokens[i + 1]);
                }
            }

            // Scan for dest flags
            if (destFlags.TryGetValue(cmdName, out var destFlagSet))
            {
                for (int i = 0; i < tokens.Count - 1; i++)
                {
                    if (destFlagSet.Contains(tokens[i]))
                        extraDests.Add((tokens[i], tokens[i + 1]));
                }
            }

            // Scan for positional exec args
            var args = tokens.Skip(1).ToList();
            if (execPos.TryGetValue(cmdName, out var execPosSet))
            {
                foreach (int idx in execPosSet)
                {
                    int actualIdx = idx < 0 ? args.Count + idx : idx;
                    if (actualIdx >= 0 && actualIdx < args.Count)
                        extraCommands.Add(args[actualIdx]);
                }
            }

            // Scan for positional dest args
            if (destPos.TryGetValue(cmdName, out var destPosSet))
            {
                foreach (int idx in destPosSet)
                {
                    int actualIdx = idx < 0 ? args.Count + idx : idx;
                    if (actualIdx >= 0 && actualIdx < args.Count)
                        extraDests.Add((idx.ToString(System.Globalization.CultureInfo.InvariantCulture), args[actualIdx]));
                }
            }
        }

        return (extraCommands, extraDests);
    }

    /// <summary>
    /// Validates that the AST contains only handled node types.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when an unhandled type is found.</exception>
    private static void CheckTypes(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        if (node.GetPropertyOrNull("Type")?.GetString() is string type && !HandledTypes.Contains(type))
            throw new InvalidOperationException($"Unhandled bash construct: {type}");

        // Recurse
        foreach (var prop in node.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
                CheckTypes(prop.Value);
            else if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.Value.EnumerateArray())
                    CheckTypes(item);
            }
        }
    }

    /// <summary>
    /// Extracts all commands, operators, and redirects from a bash command string.
    /// </summary>
    /// <remarks>
    /// This is the main entry point. It parses the command using shfmt, validates the AST,
    /// collects commands/operators/redirects, and then recursively processes nested commands
    /// from exec flags and positions.
    /// </remarks>
    public static async Task<ExtractionResult> ExtractAsync(
        string cmd,
        string shfmtPath = ShfmtDefault,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? execFlags = null,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? destFlags = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? destPos = null,
        IReadOnlyDictionary<string, IReadOnlySet<int>>? execPos = null)
    {
        if (string.IsNullOrEmpty(cmd))
            throw new ArgumentNullException(nameof(cmd));

        // Parse the bash command
        var ast = await ParseBashAsync(cmd, shfmtPath);

        // Validate AST types
        CheckTypes(ast);

        // Collect commands from the AST
        var commands = new List<List<string>>();
        if (ast.GetPropertyOrNull("Stmts") is JsonElement stmts && stmts.ValueKind == JsonValueKind.Array)
        {
            VisitStmts(stmts.EnumerateArray(), cmd, commands);
        }

        // Collect operators and redirects
        var ops = CollectOps(ast);
        var redirects = CollectRedirects(ast, cmd);

        // Scan for additional commands and destinations via flags/positions
        var (extraCommands, extraDests) = ScanFlagArgs(
            commands.Cast<IReadOnlyList<string>>(), execFlags, destFlags, destPos, execPos);

        // Recursively process nested commands
        foreach (string nestedCmd in extraCommands)
        {
            var nested = await ExtractAsync(nestedCmd, shfmtPath, execFlags, destFlags, destPos, execPos);
            commands.AddRange(nested.Commands.Select(c => c.ToList()));
            foreach (var op in nested.Operators)
                ops.Add(op);
            redirects.AddRange(nested.Redirects);
        }

        // Append extra destinations
        redirects.AddRange(extraDests);

        return new ExtractionResult(commands, ops, redirects);
    }

    /// <summary>
    /// Implements <see cref="IShellExtractor.ExtractAsync"/> by delegating to the static overload
    /// with default shfmt path and no flag/position overrides.
    /// </summary>
    Task<ExtractionResult> IShellExtractor.ExtractAsync(string cmd) =>
        ExtractAsync(cmd);

}

/// <summary>
/// Extensions for safe JSON element navigation.
/// </summary>
internal static class JsonElementExtensions
{
    /// <summary>
    /// Gets a property value or returns null if not found.
    /// </summary>
    internal static JsonElement? GetPropertyOrNull(this JsonElement element, string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        return element.TryGetProperty(propertyName, out var value) ? value : null;
    }

    /// <summary>
    /// Enumerates an array or returns an empty enumerable if not an array or not found.
    /// </summary>
    internal static IEnumerable<JsonElement> EnumerateArrayOrEmpty(this JsonElement element)
    {
        return element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray()
            : Enumerable.Empty<JsonElement>();
    }
}
