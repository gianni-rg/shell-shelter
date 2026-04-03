using System.Management.Automation.Language;

namespace ShellShelter.Core.PowerShell;

/// <summary>
/// Extracts commands, operators, and write-redirect destinations from PowerShell command strings
/// using the <c>System.Management.Automation.Language</c> parser.
/// </summary>
/// <remarks>
/// This extractor is the PowerShell equivalent of <c>BashExtractor</c>. It parses the text
/// in-process (no external process) using the same parser that PowerShell itself uses, so all
/// valid PS 7 syntax is handled correctly — including pipeline chains (<c>&amp;&amp;</c>, <c>||</c>),
/// background jobs (<c>&amp;</c>), and multi-redirection operators.
/// <para>
/// Alias normalisation via <see cref="PsAliasResolver"/> is applied before returning so the
/// caller only needs to allowlist canonical cmdlet names.
/// </para>
/// </remarks>
public sealed class PsExtractor : IShellExtractor
{
    /// <summary>
    /// Extracts all commands, operators, and write-redirect destinations from a PowerShell command string.
    /// </summary>
    /// <param name="cmd">The PowerShell command string to analyse.</param>
    /// <returns>
    /// An <see cref="ExtractionResult"/> containing every command token array (with aliases resolved),
    /// every operator string, and every write-redirect destination.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="cmd"/> is null or empty.</exception>
    public Task<ExtractionResult> ExtractAsync(string cmd)
    {
        ArgumentException.ThrowIfNullOrEmpty(cmd);

        ScriptBlockAst ast = Parser.ParseInput(cmd, out _, out ParseError[] errors);

        // Surface parse errors — not necessarily fatal; partial ASTs are still useful
        if (errors.Length > 0)
        {
            string errorMessages = string.Join("; ", errors.Select(e => e.Message));
            throw new InvalidOperationException($"PowerShell parse error(s): {errorMessages}");
        }

        var commands = new List<IReadOnlyList<string>>();
        var operators = new HashSet<string>(StringComparer.Ordinal);
        var redirects = new List<(string Op, string Dest)>();

        CollectFromAst(ast, commands, operators, redirects);

        return Task.FromResult(new ExtractionResult(commands, operators, redirects));
    }

    // ---------------------------------------------------------------------------
    // AST walk helpers
    // ---------------------------------------------------------------------------

    private static void CollectFromAst(
        Ast ast,
        List<IReadOnlyList<string>> commands,
        HashSet<string> operators,
        List<(string, string)> redirects)
    {
        // Collect all CommandAst nodes (leaf-most command invocations)
        foreach (CommandAst cmdAst in ast.FindAll(a => a is CommandAst, searchNestedScriptBlocks: true)
                                         .Cast<CommandAst>())
        {
            CollectCommand(cmdAst, commands, redirects);
        }

        // Pipeline chain operators (&& and ||) — PS 7.0+
        foreach (PipelineChainAst chainAst in ast.FindAll(a => a is PipelineChainAst, searchNestedScriptBlocks: true)
                                                  .Cast<PipelineChainAst>())
        {
            operators.Add(chainAst.Operator == TokenKind.AndAnd ? "&&" : "||");
        }

        // Pipeline separator (|) and background job operator (&)
        foreach (PipelineAst pipelineAst in ast.FindAll(a => a is PipelineAst, searchNestedScriptBlocks: true)
                                                .Cast<PipelineAst>())
        {
            if (pipelineAst.PipelineElements.Count > 1)
                operators.Add("|");

            if (pipelineAst.Background)
                operators.Add("&");
        }
        // FileRedirectionAst nodes are collected per-command in CollectCommand via CommandAst.Redirections.
        // A global FindAll walk would duplicate those entries, so it is intentionally omitted here.
    }

    private static void CollectCommand(
        CommandAst cmdAst,
        List<IReadOnlyList<string>> commands,
        List<(string, string)> redirects)
    {
        // Build the token list — first element is the command name (alias-resolved)
        var tokens = new List<string>();

        foreach (CommandElementAst element in cmdAst.CommandElements)
        {
            string text = ExtractText(element);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (tokens.Count == 0)
            {
                // Resolve alias on the command name
                tokens.Add(PsAliasResolver.Resolve(text));
            }
            else
            {
                tokens.Add(text);
            }
        }

        if (tokens.Count > 0)
            commands.Add(tokens);

        // Redirections attached directly to this CommandAst
        foreach (RedirectionAst redir in cmdAst.Redirections)
        {
            if (redir is FileRedirectionAst fileRedir)
                CollectRedirection(fileRedir, redirects);
        }
    }

    private static void CollectRedirection(
        FileRedirectionAst redirAst,
        List<(string, string)> redirects)
    {
        // Only write redirections matter for destination validation
        if (redirAst.Append == false || redirAst.Append == true)
        {
            // Any output redirection — determine operator string
            string op = redirAst.Append ? ">>" : ">";
            string? dest = ExtractText(redirAst.Location);
            if (!string.IsNullOrWhiteSpace(dest))
                redirects.Add((op, dest));
        }
    }

    /// <summary>
    /// Extracts the literal text from a CommandElementAst, handling the most common node types.
    /// </summary>
    private static string ExtractText(Ast? ast) => ast switch
    {
        null => string.Empty,
        StringConstantExpressionAst s => s.Value,
        ExpandableStringExpressionAst e => e.Value,  // unexpanded — used for dest pattern matching
        VariableExpressionAst v => "$" + v.VariablePath.UserPath,
        _ => ast.Extent.Text,  // fallback: raw source text
    };
}
