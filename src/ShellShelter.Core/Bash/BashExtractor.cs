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
    private static readonly Dictionary<int, string> OpMapLegacy = new()
    {
        // shfmt legacy op codes
        { 10, "&&" },
        { 11, "||" },
        { 12, "|" },
        { 13, "|&" },
        { 54, ">" },
        { 55, ">>" },
        { 56, "<" },
        { 58, "<&" },
        { 59, ">&" },
        { 64, "&>" },
        { 65, "&>>" }
    };

    private static readonly Dictionary<int, string> OpMapNew = new()
    {
        // shfmt newer op codes
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
    private static readonly Dictionary<int, string> WriteOpsLegacy = new()
    {
        { 54, ">" },
        { 55, ">>" },
        { 64, "&>" },
        { 65, "&>>" }
    };

    private static readonly Dictionary<int, string> WriteOpsNew = new()
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
        bool useLegacyOpCodes = UsesLegacyOperatorCodes(node);
        var ops = new HashSet<string>();
        CollectOpsRecursive(node, ops, useLegacyOpCodes);
        return ops;
    }

    /// <summary>
    /// Recursively collects operators.
    /// </summary>
    private static void CollectOpsRecursive(JsonElement node, HashSet<string> ops, bool useLegacyOpCodes)
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
        if (node.GetPropertyOrNull("Op")?.GetInt32() is int opCode
            && TryMapOperatorCode(opCode, useLegacyOpCodes, out var opStr))
            ops.Add(opStr);

        // Recurse into all properties
        foreach (var prop in node.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
                CollectOpsRecursive(prop.Value, ops, useLegacyOpCodes);
            else if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.Value.EnumerateArray())
                    CollectOpsRecursive(item, ops, useLegacyOpCodes);
            }
        }
    }

    /// <summary>
    /// Walks the AST and collects all write redirect destinations.
    /// </summary>
    private static List<(string Op, string Dest)> CollectRedirects(JsonElement node, string originalCmd)
    {
        bool useLegacyOpCodes = UsesLegacyOperatorCodes(node);
        var redirects = new List<(string, string)>();
        CollectRedirectsRecursive(node, originalCmd, redirects, useLegacyOpCodes);
        return redirects;
    }

    /// <summary>
    /// Recursively collects redirects.
    /// </summary>
    private static void CollectRedirectsRecursive(
        JsonElement node,
        string originalCmd,
        List<(string, string)> redirects,
        bool useLegacyOpCodes)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        // Check Redirs array
        if (node.GetPropertyOrNull("Redirs") is JsonElement redirList && redirList.ValueKind == JsonValueKind.Array)
        {
            foreach (var redir in redirList.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object))
            {
                int? opCode = redir.GetPropertyOrNull("Op")?.GetInt32();
                if (opCode.HasValue && TryMapWriteOperatorCode(opCode.Value, useLegacyOpCodes, out var op) &&
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
                CollectRedirectsRecursive(prop.Value, originalCmd, redirects, useLegacyOpCodes);
            else if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.Value.EnumerateArray())
                    CollectRedirectsRecursive(item, originalCmd, redirects, useLegacyOpCodes);
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

        execFlags ??= EmptyStringMap;
        destFlags ??= EmptyStringMap;
        destPos ??= EmptyIntMap;
        execPos ??= EmptyIntMap;

        foreach (var tokens in commands)
        {
            if (tokens.Count == 0)
                continue;

            CollectExecFlagCommands(tokens, execFlags, extraCommands);
            CollectDestFlagArguments(tokens, destFlags, extraDests);
            CollectExecPosCommands(tokens, execPos, extraCommands);
            CollectDestPosArguments(tokens, destPos, extraDests);
        }

        return (extraCommands, extraDests);
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> EmptyStringMap
        = new Dictionary<string, IReadOnlySet<string>>();

    private static readonly IReadOnlyDictionary<string, IReadOnlySet<int>> EmptyIntMap
        = new Dictionary<string, IReadOnlySet<int>>();

    private static void CollectExecFlagCommands(
        IReadOnlyList<string> tokens,
        IReadOnlyDictionary<string, IReadOnlySet<string>> execFlags,
        ICollection<string> extraCommands)
    {
        foreach (var flagSet in GetMostSpecificMatches(tokens, execFlags).Select(static match => match.Value))
            CollectFlagArguments(tokens, flagSet, static (op, arg) => arg, extraCommands);
    }

    private static void CollectDestFlagArguments(
        IReadOnlyList<string> tokens,
        IReadOnlyDictionary<string, IReadOnlySet<string>> destFlags,
        ICollection<(string, string)> extraDests)
    {
        foreach (var flagSet in GetMostSpecificMatches(tokens, destFlags).Select(static match => match.Value))
            CollectFlagArguments(tokens, flagSet, static (op, arg) => (op, arg), extraDests);
    }

    private static void CollectExecPosCommands(
        IReadOnlyList<string> tokens,
        IReadOnlyDictionary<string, IReadOnlySet<int>> execPos,
        ICollection<string> extraCommands)
    {
        foreach (var match in GetMostSpecificMatches(tokens, execPos))
            CollectPositionalArguments(tokens, match, static (_, arg) => arg, extraCommands);
    }

    private static void CollectDestPosArguments(
        IReadOnlyList<string> tokens,
        IReadOnlyDictionary<string, IReadOnlySet<int>> destPos,
        ICollection<(string, string)> extraDests)
    {
        foreach (var match in GetMostSpecificMatches(tokens, destPos))
            CollectPositionalArguments(
                tokens,
                match,
                static (idx, arg) => (idx.ToString(System.Globalization.CultureInfo.InvariantCulture), arg),
                extraDests);
    }

    private static void CollectFlagArguments<T>(
        IReadOnlyList<string> tokens,
        IReadOnlySet<string> flagSet,
        Func<string, string, T> projection,
        ICollection<T> output)
    {
        for (int index = 0; index < tokens.Count - 1; index++)
        {
            string token = tokens[index];
            if (!flagSet.Contains(token))
                continue;

            output.Add(projection(token, tokens[index + 1]));
        }
    }

    private static void CollectPositionalArguments<T>(
        IReadOnlyList<string> tokens,
        (int KeyTokenCount, IReadOnlySet<int> Value) match,
        Func<int, string, T> projection,
        ICollection<T> output)
    {
        var args = tokens.Skip(match.KeyTokenCount).ToList();
        foreach (int idx in match.Value)
        {
            int actualIdx = idx < 0 ? args.Count + idx : idx;
            if (actualIdx < 0 || actualIdx >= args.Count)
                continue;

            output.Add(projection(idx, args[actualIdx]));
        }
    }

    internal static bool TryMapOperatorCode(int opCode, bool useLegacyOpCodes, out string op)
    {
        return (useLegacyOpCodes ? OpMapLegacy : OpMapNew).TryGetValue(opCode, out op!);
    }

    internal static bool TryMapWriteOperatorCode(int opCode, bool useLegacyOpCodes, out string op)
    {
        return (useLegacyOpCodes ? WriteOpsLegacy : WriteOpsNew).TryGetValue(opCode, out op!);
    }

    private static bool UsesLegacyOperatorCodes(JsonElement node)
    {
        var opCodes = new HashSet<int>();
        CollectOpCodes(node, opCodes);
        return opCodes.Overlaps([10, 54, 55, 56, 58, 59]);
    }

    private static void CollectOpCodes(JsonElement node, HashSet<int> opCodes)
    {
        if (node.ValueKind != JsonValueKind.Object)
            return;

        if (node.GetPropertyOrNull("Op")?.GetInt32() is int opCode)
            opCodes.Add(opCode);

        foreach (var prop in node.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Object)
            {
                CollectOpCodes(prop.Value, opCodes);
            }
            else if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in prop.Value.EnumerateArray())
                    CollectOpCodes(item, opCodes);
            }
        }
    }

    private static List<(int KeyTokenCount, T Value)> GetMostSpecificMatches<T>(
        IReadOnlyList<string> commandTokens,
        IReadOnlyDictionary<string, T> map)
    {
        var matches = new List<(int KeyTokenCount, T Value)>();
        int maxTokenCount = 0;

        foreach (var kvp in map)
        {
            int keyTokenCount = CountMatchingPrefixTokens(kvp.Key, commandTokens);
            if (keyTokenCount == 0)
                continue;

            if (keyTokenCount > maxTokenCount)
            {
                matches.Clear();
                maxTokenCount = keyTokenCount;
            }

            if (keyTokenCount == maxTokenCount)
                matches.Add((keyTokenCount, kvp.Value));
        }

        return matches;
    }

    private static int CountMatchingPrefixTokens(string key, IReadOnlyList<string> commandTokens)
    {
        string[] keyTokens = key.Split(
            [' ', '\t', '\r', '\n', '\f', '\v'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (keyTokens.Length == 0 || keyTokens.Length > commandTokens.Count)
            return 0;

        for (int index = 0; index < keyTokens.Length; index++)
        {
            if (!string.Equals(keyTokens[index], commandTokens[index], StringComparison.Ordinal))
                return 0;
        }

        return keyTokens.Length;
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
