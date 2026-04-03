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
        result.Operators.SetEquals(expectedOps ?? new HashSet<string>()).ShouldBeTrue();
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
            return proc is not null
                && proc.WaitForExit(5000)
                && proc.ExitCode == 0;
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
    public async Task Extract_FindWithExecFlag_ExtractsNestedCommand()
    {
        SkipIfShfmtMissing();
        var execFlags = new Dictionary<string, IReadOnlySet<string>>
        {
            { "find", new HashSet<string> { "-exec", "-execdir" } },
            { "tar", new HashSet<string> { "--to-command", "-I" } }
        };

        var result = await BashExtractor.ExtractAsync(
            "find . -exec ls",
            execFlags: execFlags);

        result.Commands.Select(c => c.ToList()).ShouldBe(
            new[] { new[] { "find", ".", "-exec", "ls" }, new[] { "ls" } }
                .Select(c => (IReadOnlyList<string>)c.ToList()).ToList());
    }

    [Fact]
    public async Task Extract_FindWithExecAndSemicolon_ExtractsNestedCommand()
    {
        SkipIfShfmtMissing();
        var execFlags = new Dictionary<string, IReadOnlySet<string>>
        {
            { "find", new HashSet<string> { "-exec", "-execdir" } },
            { "tar", new HashSet<string> { "--to-command", "-I" } }
        };

        var result = await BashExtractor.ExtractAsync(
            "find . -exec rm -rf {} \\;",
            execFlags: execFlags);

        var actualCmds = result.Commands.Select(c => c.ToList()).ToList();
        actualCmds.Count.ShouldBe(2);
        actualCmds[0].ShouldBe(new[] { "find", ".", "-exec", "rm", "-rf", "{}", "\\;" });
        actualCmds[1].ShouldBe(new[] { "rm" });
    }

    [Fact]
    public async Task Extract_CurlWithOutputFlag_ExtractsDestination()
    {
        SkipIfShfmtMissing();
        var destFlags = new Dictionary<string, IReadOnlySet<string>>
        {
            { "curl", new HashSet<string> { "-o", "--output" } }
        };

        var result = await BashExtractor.ExtractAsync(
            "curl -o /tmp/out http://x",
            destFlags: destFlags);

        result.Commands.Select(c => c.ToList()).ShouldBe(
            new[] { new[] { "curl", "-o", "/tmp/out", "http://x" } }
                .Select(c => (IReadOnlyList<string>)c.ToList()).ToList());
        result.Redirects.ShouldBe(new[] { ("-o", "/tmp/out") });
    }

    [Fact]
    public async Task Extract_CurlWithLongOutputFlag_ExtractsDestination()
    {
        SkipIfShfmtMissing();
        var destFlags = new Dictionary<string, IReadOnlySet<string>>
        {
            { "curl", new HashSet<string> { "-o", "--output" } }
        };

        var result = await BashExtractor.ExtractAsync(
            "curl --output file.txt http://x",
            destFlags: destFlags);

        result.Redirects.ShouldBe(new[] { ("--output", "file.txt") });
    }

    [Fact]
    public async Task Extract_ExWithDestPosition_ExtractsDestination()
    {
        SkipIfShfmtMissing();
        var destPos = new Dictionary<string, IReadOnlySet<int>>
        {
            { "ex", new HashSet<int> { 0 } },
            { "tee", new HashSet<int> { 0 } },
            { "cp", new HashSet<int> { -1 } },
            { "mv", new HashSet<int> { -1 } }
        };

        var result = await BashExtractor.ExtractAsync(
            "ex somefile",
            destPos: destPos);

        result.Redirects.Count.ShouldBe(1);
        result.Redirects[0].Op.ShouldBe("0");
        result.Redirects[0].Dest.ShouldBe("somefile");
    }

    [Fact]
    public async Task Extract_CpWithDestPosition_ExtractsLastArg()
    {
        SkipIfShfmtMissing();
        var destPos = new Dictionary<string, IReadOnlySet<int>>
        {
            { "ex", new HashSet<int> { 0 } },
            { "tee", new HashSet<int> { 0 } },
            { "cp", new HashSet<int> { -1 } },
            { "mv", new HashSet<int> { -1 } }
        };

        var result = await BashExtractor.ExtractAsync(
            "cp src.txt dest.txt",
            destPos: destPos);

        result.Redirects.Count.ShouldBe(1);
        result.Redirects[0].Op.ShouldBe("-1");
        result.Redirects[0].Dest.ShouldBe("dest.txt");
    }

    [Fact]
    public async Task Extract_EnvWithExecPosition_ExtractsNestedCommand()
    {
        SkipIfShfmtMissing();
        var execPos = new Dictionary<string, IReadOnlySet<int>>
        {
            { "env", new HashSet<int> { 0 } },
            { "xargs", new HashSet<int> { 0 } }
        };

        var result = await BashExtractor.ExtractAsync(
            "env ls -la",
            execPos: execPos);

        var actualCmds = result.Commands.Select(c => c.ToList()).ToList();
        actualCmds.Count.ShouldBe(2);
        actualCmds[0].ShouldBe(new[] { "env", "ls", "-la" });
        actualCmds[1].ShouldBe(new[] { "ls" });
    }

    [Fact]
    public async Task Extract_XargsWithExecPosition_ExtractsNestedCommand()
    {
        SkipIfShfmtMissing();
        var execPos = new Dictionary<string, IReadOnlySet<int>>
        {
            { "env", new HashSet<int> { 0 } },
            { "xargs", new HashSet<int> { 0 } }
        };

        var result = await BashExtractor.ExtractAsync(
            "xargs grep pattern",
            execPos: execPos);

        var actualCmds = result.Commands.Select(c => c.ToList()).ToList();
        actualCmds.Count.ShouldBe(2);
        actualCmds[0].ShouldBe(new[] { "xargs", "grep", "pattern" });
        actualCmds[1].ShouldBe(new[] { "grep" });
    }

    [Fact]
    public async Task Extract_MostSpecificExecPosRule_UsesFullCommandPrefix()
    {
        SkipIfShfmtMissing();
        var execPos = new Dictionary<string, IReadOnlySet<int>>
        {
            { "git", new HashSet<int> { 0 } },
            { "git clone", new HashSet<int> { 0 } }
        };

        var result = await BashExtractor.ExtractAsync(
            "git clone ls destination",
            execPos: execPos);

        var actualCmds = result.Commands.Select(c => c.ToList()).ToList();
        actualCmds.Count.ShouldBe(2);
        actualCmds[0].ShouldBe(new[] { "git", "clone", "ls", "destination" });
        actualCmds[1].ShouldBe(new[] { "ls" });
    }

    [Fact]
    public void TryMapOperatorCode_LegacyAndNewModes_MapExpectedValues()
    {
        BashExtractor.TryMapOperatorCode(10, useLegacyOpCodes: true, out string legacyAnd).ShouldBeTrue();
        legacyAnd.ShouldBe("&&");

        BashExtractor.TryMapOperatorCode(11, useLegacyOpCodes: false, out string newAnd).ShouldBeTrue();
        newAnd.ShouldBe("&&");

        BashExtractor.TryMapOperatorCode(11, useLegacyOpCodes: true, out string legacyOr).ShouldBeTrue();
        legacyOr.ShouldBe("||");
    }

    [Fact]
    public void TryMapWriteOperatorCode_LegacyAndNewModes_MapExpectedValues()
    {
        BashExtractor.TryMapWriteOperatorCode(54, useLegacyOpCodes: true, out string legacyWrite).ShouldBeTrue();
        legacyWrite.ShouldBe(">");

        BashExtractor.TryMapWriteOperatorCode(63, useLegacyOpCodes: false, out string newWrite).ShouldBeTrue();
        newWrite.ShouldBe(">");
    }

    [Fact]
    public async Task Extract_UnhandledConstruct_ThrowsInvalidOperation()
    {
        SkipIfShfmtMissing();
        // [[ -f foo ]] uses the TestExpression construct which is not handled
        await Should.ThrowAsync<InvalidOperationException>(
            () => BashExtractor.ExtractAsync("[[ -f foo ]]"));
    }

    [Fact]
    public async Task Extract_EmptyCommand_ThrowsArgumentNull()
    {
        SkipIfShfmtMissing();
        await Should.ThrowAsync<ArgumentNullException>(
            () => BashExtractor.ExtractAsync(""));
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

    // Round-4 PR #1 comment: UsesLegacyOperatorCodes was too narrow — it only checked for
    // codes exclusively in the legacy set. Codes that are shared between maps but have
    // DIFFERENT meanings (e.g. 12 = '|' in legacy vs '||' in new) can also appear in a
    // legacy-only AST and must be mapped correctly. These tests directly assert that the
    // opcode tables and TryMapOperatorCode resolve the correct string for each mode.

    [Theory]
    [InlineData(12, true,  "|")]   // shared code: legacy meaning = pipe
    [InlineData(12, false, "||")]  // shared code: new meaning = logical OR
    [InlineData(11, true,  "||")]  // shared code: legacy meaning = logical OR
    [InlineData(11, false, "&&")]  // shared code: new meaning = logical AND
    [InlineData(13, true,  "|&")]  // shared code: legacy meaning = pipe-with-stderr
    [InlineData(13, false, "|")]   // shared code: new meaning = pipe
    public void TryMapOperatorCode_SharedOpcode_MapsAccordingToMode(int code, bool legacy, string expected)
    {
        bool found = BashExtractor.TryMapOperatorCode(code, legacy, out string op);

        found.ShouldBeTrue();
        op.ShouldBe(expected);
    }

    [Theory]
    [InlineData(10, "&&")]  // legacy-only: logical AND
    [InlineData(54, ">")]   // legacy-only: redirect out
    [InlineData(55, ">>")]  // legacy-only: redirect out append
    public void TryMapOperatorCode_LegacyOnlyOpcode_FoundInLegacyMapOnly(int code, string expected)
    {
        bool legacy_found = BashExtractor.TryMapOperatorCode(code, useLegacyOpCodes: true, out string legacyOp);
        bool new_found    = BashExtractor.TryMapOperatorCode(code, useLegacyOpCodes: false, out _);

        legacy_found.ShouldBeTrue();
        legacyOp.ShouldBe(expected);
        new_found.ShouldBeFalse();
    }

    [Theory]
    [InlineData(14,  "|&")]  // new-only: pipe-with-stderr
    [InlineData(63,  ">")]   // new-only: redirect out
    [InlineData(67,  "<&")]  // new-only: fd dup read
    [InlineData(68,  ">&")]  // new-only: fd dup write
    [InlineData(74,  "&>")]  // new-only: redirect stdout+stderr
    [InlineData(76,  "&>>")] // new-only: redirect stdout+stderr append
    public void TryMapOperatorCode_NewOnlyOpcode_FoundInNewMapOnly(int code, string expected)
    {
        bool new_found    = BashExtractor.TryMapOperatorCode(code, useLegacyOpCodes: false, out string newOp);
        bool legacy_found = BashExtractor.TryMapOperatorCode(code, useLegacyOpCodes: true, out _);

        new_found.ShouldBeTrue();
        newOp.ShouldBe(expected);
        legacy_found.ShouldBeFalse();
    }
}
