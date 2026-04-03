using System.Diagnostics;
using Shouldly;
using ShellShelter.Core.Bash;

namespace ShellShelter.Tests;

/// <summary>
/// Tests for BashExtractor module. Tests follow Python bashxtract.ipynb test cases.
/// </summary>
public sealed class BashExtractorTests
{
    /// <summary>
    /// Helper to assert extraction results match expected values.
    /// </summary>
    private static async Task AssertExtractionAsync(
        string cmd,
        IReadOnlyList<IReadOnlyList<string>> expectedCommands,
        IReadOnlySet<string>? expectedOps = null,
        IReadOnlyList<(string Op, string Dest)>? expectedRedirects = null)
    {
        var result = await BashExtractor.ExtractAsync(cmd);

        // Convert commands to list of lists for easier comparison
        var actualCmds = result.Commands.Select(c => c.ToList()).ToList();
        var expectedCmds = expectedCommands.Select(c => c.ToList()).ToList();

        actualCmds.ShouldBe(expectedCmds);
        result.Operators.ShouldBe(expectedOps ?? new HashSet<string>());
        result.Redirects.ShouldBe(expectedRedirects ?? new List<(string, string)>());
    }
    /// <summary>
    /// Helper to assert extraction results match expected values (sync wrapper).
    /// </summary>
    private static bool IsShfmtAvailable()
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo("shfmt", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            proc?.WaitForExit(5000);
            return true;
        }
        catch { return false; }
    }

    private static void AssertExtraction(
        string cmd,
        IReadOnlyList<IReadOnlyList<string>> expectedCommands,
        IReadOnlySet<string>? expectedOps = null,
        IReadOnlyList<(string Op, string Dest)>? expectedRedirects = null)
    {
        SkipIfShfmtMissing();
        AssertExtractionAsync(cmd, expectedCommands, expectedOps, expectedRedirects).Wait();
    }

    private static void SkipIfShfmtMissing()
    {
        if (!IsShfmtAvailable())
            Assert.Skip(
                "shfmt not found in PATH. Install: winget install mvdan.shfmt (Windows), " +
                "brew install shfmt (macOS), apt-get install shfmt (Linux).");
    }
    [Fact]
    public void Extract_SimpleCommand_ReturnsCommand()
    {
        AssertExtraction("echo hello", new[] { new[] { "echo", "hello" } });
    }

    [Fact]
    public void Extract_Heredoc_InlinesContent()
    {
        string cmd = "echo <<EOF\nasdf\njkljl\nEOF\n";
        AssertExtraction(cmd, new[] { new[] { "echo", "asdf\njkljl" } });
    }

    [Fact]
    public void Extract_CommandSubstitution_ExtractsNestedCommand()
    {
        AssertExtraction(
            "echo $(foo)",
            new[] { new[] { "echo", "$(foo)" }, new[] { "foo" } });
    }

    [Fact]
    public void Extract_CommandSubstitutionWithPipe_ExtractsAll()
    {
        AssertExtraction(
            "echo $(foo) | cat -a",
            new[] { new[] { "echo", "$(foo)" }, new[] { "foo" }, new[] { "cat", "-a" } },
            expectedOps: new HashSet<string> { "|" });
    }

    [Fact]
    public void Extract_NestedCommandSubstitution_ExtractsRecursively()
    {
        AssertExtraction(
            "echo $(cat $(ls))",
            new[]
            {
                new[] { "echo", "$(cat $(ls))" },
                new[] { "cat", "$(ls)" },
                new[] { "ls" }
            });
    }

    [Fact]
    public void Extract_QuotedString_PreservesAsToken()
    {
        AssertExtraction(
            "echo \"hello world\" foo",
            new[] { new[] { "echo", "hello world", "foo" } });
    }

    [Fact]
    public void Extract_EscapedSpace_ConvertedToSpace()
    {
        AssertExtraction(
            "echo hello\\ world",
            new[] { new[] { "echo", "hello world" } });
    }

    [Fact]
    public void Extract_SequencedCommands_ExtractsBoth()
    {
        AssertExtraction(
            "echo foo; echo bar",
            new[] { new[] { "echo", "foo" }, new[] { "echo", "bar" } },
            expectedOps: new HashSet<string> { ";" });
    }

    [Fact]
    public void Extract_EnvironmentVariables_PreservedInOutput()
    {
        AssertExtraction(
            "echo $HOME \"${USER}\"",
            new[] { new[] { "echo", "$HOME", "${USER}" } });
    }

    [Fact]
    public void Extract_BackgroundJob_IncludesAmpersandOp()
    {
        AssertExtraction(
            "sleep 10 &",
            new[] { new[] { "sleep", "10" } },
            expectedOps: new HashSet<string> { ";", "&" });
    }

    [Fact]
    public void Extract_HereString_InlinesContent()
    {
        AssertExtraction(
            "cat <<< \"some text\"",
            new[] { new[] { "cat", "<<<", "some text" } });
    }

    [Fact]
    public void Extract_QuotedWithSingleQuotes_Preserved()
    {
        AssertExtraction(
            "echo \"it's a 'test'\"",
            new[] { new[] { "echo", "it's a 'test'" } });
    }

    [Fact]
    public void Extract_CommandSubstitutionInDouble_ExtractsNested()
    {
        AssertExtraction(
            "echo \"hello $(whoami) there\"",
            new[] { new[] { "echo", "hello $(whoami) there" }, new[] { "whoami" } });
    }

    [Fact]
    public void Extract_ParameterExpansionInDouble_Preserved()
    {
        AssertExtraction(
            "echo \"path is ${HOME}/bin\"",
            new[] { new[] { "echo", "path is ${HOME}/bin" } });
    }

    [Fact]
    public void Extract_ArrayIndexing_Preserved()
    {
        AssertExtraction(
            "echo ${arr[0]}",
            new[] { new[] { "echo", "${arr[0]}" } });
    }

    [Fact]
    public void Extract_NestedQuotedCommandSubstitution_ExtractsRecursively()
    {
        AssertExtraction(
            "echo \"$(echo \"inner\")\"",
            new[]
            {
                new[] { "echo", "$(echo \"inner\")" },
                new[] { "echo", "inner" }
            });
    }

    [Fact]
    public void Extract_MixedSubstInQuoted_ExtractsVariableAndCommand()
    {
        AssertExtraction(
            "echo \"$HOME/$(whoami)/file\"",
            new[]
            {
                new[] { "echo", "$HOME/$(whoami)/file" },
                new[] { "whoami" }
            });
    }

    [Fact]
    public void Extract_BacktickSubstitution_ExtractsNestedCommand()
    {
        AssertExtraction(
            "echo `whoami`",
            new[] { new[] { "echo", "`whoami`" }, new[] { "whoami" } });
    }

    [Fact]
    public void Extract_Subshell_ExtractsCommandsInside()
    {
        AssertExtraction(
            "(cd /tmp && rm -rf *)",
            new[] { new[] { "cd", "/tmp" }, new[] { "rm", "-rf", "*" } },
            expectedOps: new HashSet<string> { "&&" });
    }

    [Fact]
    public void Extract_EvalString_TreatsAsLiteral()
    {
        AssertExtraction(
            "eval \"rm -rf /\"",
            new[] { new[] { "eval", "rm -rf /" } });
    }

    [Fact]
    public void Extract_LogicalAndOr_ExtractsAllCommands()
    {
        AssertExtraction(
            "echo a && echo b || echo c",
            new[] { new[] { "echo", "a" }, new[] { "echo", "b" }, new[] { "echo", "c" } },
            expectedOps: new HashSet<string> { "&&", "||" });
    }

    [Fact]
    public void Extract_PipeToFile_ExtractsCommand()
    {
        AssertExtraction(
            "cat file > out",
            new[] { new[] { "cat", "file" } },
            expectedOps: new HashSet<string> { ">" },
            expectedRedirects: new[] { (">", "out") });
    }

    [Fact]
    public void Extract_AppendRedirect_ExtractsCommand()
    {
        AssertExtraction(
            "cat file >> out",
            new[] { new[] { "cat", "file" } },
            expectedOps: new HashSet<string> { ">>" },
            expectedRedirects: new[] { (">>", "out") });
    }

    [Fact]
    public void Extract_InputRedirect_NoRedirectsCollected()
    {
        AssertExtraction(
            "cat < in",
            new[] { new[] { "cat" } },
            expectedOps: new HashSet<string> { "<" });
    }

    [Fact]
    public void Extract_ProcessSubstitution_ExtractsNestedCommands()
    {
        AssertExtraction(
            "diff <(ls dir1) <(ls dir2)",
            new[]
            {
                new[] { "diff", "<(ls dir1)", "<(ls dir2)" },
                new[] { "ls", "dir1" },
                new[] { "ls", "dir2" }
            });
    }

    [Fact]
    public void Extract_Assignment_ExtractsAssignmentOp()
    {
        AssertExtraction(
            "FOO=bar",
            System.Array.Empty<string[]>(),
            expectedOps: new HashSet<string> { "=" });
    }

    [Fact]
    public void Extract_AssignmentBeforeCommand_ExtractsCommand()
    {
        AssertExtraction(
            "FOO=bar echo hello",
            new[] { new[] { "echo", "hello" } },
            expectedOps: new HashSet<string> { "=" });
    }

    [Fact]
    public void Extract_ForLoop_ExtractsLoopBody()
    {
        AssertExtraction(
            "for i in a b c; do echo $i; done",
            new[] { new[] { "echo", "$i" } },
            expectedOps: new HashSet<string> { ";" });
    }

    [Fact]
    public void Extract_StdoutAndStderrRedirect_Extracted()
    {
        AssertExtraction(
            "echo &>file",
            new[] { new[] { "echo" } },
            expectedOps: new HashSet<string> { "&>" },
            expectedRedirects: new[] { ("&>", "file") });
    }

    [Fact]
    public void Extract_StdoutAndStderrAppend_Extracted()
    {
        AssertExtraction(
            "echo &>>file",
            new[] { new[] { "echo" } },
            expectedOps: new HashSet<string> { "&>>" },
            expectedRedirects: new[] { ("&>>", "file") });
    }

    [Fact]
    public void Extract_PipeToBackground_ExtractsCommand()
    {
        AssertExtraction(
            "echo |& cat",
            new[] { new[] { "echo" }, new[] { "cat" } },
            expectedOps: new HashSet<string> { "|&" });
    }

    [Fact]
    public void Extract_StderrDuplication_NoRedirectsCollected()
    {
        AssertExtraction(
            "echo >&2",
            new[] { new[] { "echo" } },
            expectedOps: new HashSet<string> { ">&" });
    }

    [Fact]
    public void Extract_StdinDuplication_NoRedirectsCollected()
    {
        AssertExtraction(
            "cat <&3",
            new[] { new[] { "cat" } },
            expectedOps: new HashSet<string> { "<&" });
    }

    [Fact]
    public void Extract_SedInPlace_PreservesArguments()
    {
        AssertExtraction(
            "sed -i '' 's/foo/bar/' file.txt",
            new[] { new[] { "sed", "-i", "", "s/foo/bar/", "file.txt" } });
    }

    [Fact]
    public void Extract_FindWithExecFlag_ExtractsNestedCommand()
    {
        SkipIfShfmtMissing();
        var execFlags = new Dictionary<string, IReadOnlySet<string>>
        {
            { "find", new HashSet<string> { "-exec", "-execdir" } },
            { "tar", new HashSet<string> { "--to-command", "-I" } }
        };

        var result = BashExtractor.ExtractAsync(
            "find . -exec ls",
            execFlags: execFlags).Result;

        result.Commands.Select(c => c.ToList()).ToList().ShouldBe(
            new[] { new[] { "find", ".", "-exec", "ls" }, new[] { "ls" } }
                .Select(c => (IReadOnlyList<string>)c.ToList()).ToList());
    }

    [Fact]
    public void Extract_FindWithExecAndSemicolon_ExtractsNestedCommand()
    {
        SkipIfShfmtMissing();
        var execFlags = new Dictionary<string, IReadOnlySet<string>>
        {
            { "find", new HashSet<string> { "-exec", "-execdir" } },
            { "tar", new HashSet<string> { "--to-command", "-I" } }
        };

        var result = BashExtractor.ExtractAsync(
            "find . -exec rm -rf {} \\;",
            execFlags: execFlags).Result;

        var actualCmds = result.Commands.Select(c => c.ToList()).ToList();
        actualCmds.Count.ShouldBe(2);
        actualCmds[0].ShouldBe(new[] { "find", ".", "-exec", "rm", "-rf", "{}", "\\;" });
        actualCmds[1].ShouldBe(new[] { "rm" });
    }

    [Fact]
    public void Extract_CurlWithOutputFlag_ExtractsDestination()
    {
        SkipIfShfmtMissing();
        var destFlags = new Dictionary<string, IReadOnlySet<string>>
        {
            { "curl", new HashSet<string> { "-o", "--output" } }
        };

        var result = BashExtractor.ExtractAsync(
            "curl -o /tmp/out http://x",
            destFlags: destFlags).Result;

        result.Commands.Select(c => c.ToList()).ToList().ShouldBe(
            new[] { new[] { "curl", "-o", "/tmp/out", "http://x" } }
                .Select(c => (IReadOnlyList<string>)c.ToList()).ToList());
        result.Redirects.ShouldBe(new[] { ("-o", "/tmp/out") });
    }

    [Fact]
    public void Extract_CurlWithLongOutputFlag_ExtractsDestination()
    {
        SkipIfShfmtMissing();
        var destFlags = new Dictionary<string, IReadOnlySet<string>>
        {
            { "curl", new HashSet<string> { "-o", "--output" } }
        };

        var result = BashExtractor.ExtractAsync(
            "curl --output file.txt http://x",
            destFlags: destFlags).Result;

        result.Redirects.ShouldBe(new[] { ("--output", "file.txt") });
    }

    [Fact]
    public void Extract_ExWithDestPosition_ExtractsDestination()
    {
        SkipIfShfmtMissing();
        var destPos = new Dictionary<string, IReadOnlySet<int>>
        {
            { "ex", new HashSet<int> { 0 } },
            { "tee", new HashSet<int> { 0 } },
            { "cp", new HashSet<int> { -1 } },
            { "mv", new HashSet<int> { -1 } }
        };

        var result = BashExtractor.ExtractAsync(
            "ex somefile",
            destPos: destPos).Result;

        result.Redirects.Count.ShouldBe(1);
        result.Redirects[0].Op.ShouldBe("0");
        result.Redirects[0].Dest.ShouldBe("somefile");
    }

    [Fact]
    public void Extract_CpWithDestPosition_ExtractsLastArg()
    {
        SkipIfShfmtMissing();
        var destPos = new Dictionary<string, IReadOnlySet<int>>
        {
            { "ex", new HashSet<int> { 0 } },
            { "tee", new HashSet<int> { 0 } },
            { "cp", new HashSet<int> { -1 } },
            { "mv", new HashSet<int> { -1 } }
        };

        var result = BashExtractor.ExtractAsync(
            "cp src.txt dest.txt",
            destPos: destPos).Result;

        result.Redirects.Count.ShouldBe(1);
        result.Redirects[0].Op.ShouldBe("-1");
        result.Redirects[0].Dest.ShouldBe("dest.txt");
    }

    [Fact]
    public void Extract_EnvWithExecPosition_ExtractsNestedCommand()
    {
        SkipIfShfmtMissing();
        var execPos = new Dictionary<string, IReadOnlySet<int>>
        {
            { "env", new HashSet<int> { 0 } },
            { "xargs", new HashSet<int> { 0 } }
        };

        var result = BashExtractor.ExtractAsync(
            "env ls -la",
            execPos: execPos).Result;

        var actualCmds = result.Commands.Select(c => c.ToList()).ToList();
        actualCmds.Count.ShouldBe(2);
        actualCmds[0].ShouldBe(new[] { "env", "ls", "-la" });
        actualCmds[1].ShouldBe(new[] { "ls" });
    }

    [Fact]
    public void Extract_XargsWithExecPosition_ExtractsNestedCommand()
    {
        SkipIfShfmtMissing();
        var execPos = new Dictionary<string, IReadOnlySet<int>>
        {
            { "env", new HashSet<int> { 0 } },
            { "xargs", new HashSet<int> { 0 } }
        };

        var result = BashExtractor.ExtractAsync(
            "xargs grep pattern",
            execPos: execPos).Result;

        var actualCmds = result.Commands.Select(c => c.ToList()).ToList();
        actualCmds.Count.ShouldBe(2);
        actualCmds[0].ShouldBe(new[] { "xargs", "grep", "pattern" });
        actualCmds[1].ShouldBe(new[] { "grep" });
    }

    [Fact]
    public void Extract_UnhandledConstruct_ThrowsInvalidOperation()
    {
        SkipIfShfmtMissing();
        // [[ -f foo ]] uses the TestExpression construct which is not handled
        Should.Throw<InvalidOperationException>(
            () => BashExtractor.ExtractAsync("[[ -f foo ]]").Result);
    }

    [Fact]
    public void Extract_EmptyCommand_ThrowsArgumentNull()
    {
        SkipIfShfmtMissing();
        Should.Throw<ArgumentNullException>(
            () => BashExtractor.ExtractAsync("").Result);
    }

    [Fact]
    public async Task Extract_NullCommand_ThrowsArgumentNull()
    {
        await Should.ThrowAsync<ArgumentNullException>(
            () => BashExtractor.ExtractAsync(null!));
    }

    [Fact]
    public async Task Extract_ShfmtNotInPath_ThrowsFileNotFound()
    {
        await Should.ThrowAsync<FileNotFoundException>(
            () => BashExtractor.ExtractAsync("echo hi", "/nonexistent/shfmt"));
    }
}
