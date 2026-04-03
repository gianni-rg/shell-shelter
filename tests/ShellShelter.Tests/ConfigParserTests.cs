using System.Text.Json;
using Shouldly;
using ShellShelter.Core;
using ShellShelter.Core.Config;

namespace ShellShelter.Tests;

public sealed class ConfigParserTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsShellPolicies()
    {
        JsonConfigParser parser = new();

        ShellPolicyPair result = parser.Parse(
            """
            {
              "bash": {
                "okCmds": [" cat ", "git status"],
                "okDests": [" ./ ", "/tmp"]
              },
              "powershell": {
                "okCmds": ["Get-ChildItem"],
                "okDests": [".\\", "$env:TEMP"]
              }
            }
            """);

        result.BashPolicy.OkCmds.OrderBy(static spec => string.Join(" ", spec.Name), StringComparer.Ordinal)
            .ShouldBe([new CmdSpec("cat"), new CmdSpec("git status")]);
        result.BashPolicy.OkDests.OrderBy(static dest => dest, StringComparer.Ordinal).ShouldBe(["./", "/tmp"]);
        result.PsPolicy.OkCmds.OrderBy(static spec => string.Join(" ", spec.Name), StringComparer.Ordinal)
            .ShouldBe([new CmdSpec("Get-ChildItem")]);
        result.PsPolicy.OkDests.OrderBy(static dest => dest, StringComparer.Ordinal).ShouldBe(["$env:TEMP", ".\\"]);
    }

    [Fact]
    public void Parse_MissingSections_ReturnsEmptyPolicies()
    {
        JsonConfigParser parser = new();

        ShellPolicyPair result = parser.Parse("{}");

        result.BashPolicy.OkCmds.ShouldBeEmpty();
        result.BashPolicy.OkDests.ShouldBeEmpty();
        result.PsPolicy.OkCmds.ShouldBeEmpty();
        result.PsPolicy.OkDests.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsJsonException()
    {
        JsonConfigParser parser = new();

        Should.Throw<JsonException>(() => parser.Parse("{"));
    }

    [Fact]
    public void LoadFromText_JsonContentWithoutExtension_UsesContentSniffing()
    {
        ConfigLoader loader = new();

        ShellPolicyPair result = loader.LoadFromText(
            """
            {
              "bash": {
                "okCmds": ["cat"]
              }
            }
            """);

        result.BashPolicy.OkCmds.ShouldBe([new CmdSpec("cat")]);
        result.PsPolicy.OkCmds.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_IniContent_ReadsDefaultSectionWithPythonCompatibility()
    {
        IniConfigParser parser = new();

        ShellPolicyPair result = parser.Parse(
            """
            [DEFAULT]
            ok_cmds = cat, git log
                # ignored comment
                env:exec=$0
            """);

        result.BashPolicy.OkDests.OrderBy(static dest => dest, StringComparer.Ordinal).ShouldBe(["./", "/tmp"]);
        result.BashPolicy.OkCmds.OrderBy(static spec => string.Join(" ", spec.Name), StringComparer.Ordinal)
            .ShouldBe([new CmdSpec("cat"), new CmdSpec("env", execPos: [0]), new CmdSpec("git log")]);
        result.PsPolicy.OkCmds.ShouldBeEmpty();
    }

    [Fact]
    public void Load_JsonFile_LoadsPolicies()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.json");

        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "powershell": {
                    "okCmds": ["Get-Content"]
                  }
                }
                """);

            ConfigLoader loader = new();

            ShellPolicyPair result = loader.Load(path);

            result.BashPolicy.OkCmds.ShouldBeEmpty();
            result.PsPolicy.OkCmds.ShouldBe([new CmdSpec("Get-Content")]);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void LoadFromText_DefaultIni_LoadsAuthoritativePythonDefaults()
    {
        ConfigLoader loader = new();

        ShellPolicyPair result = loader.LoadFromText(DefaultConfigs.BashDefaultIni, "config.ini");

        result.BashPolicy.OkDests.OrderBy(static dest => dest, StringComparer.Ordinal).ShouldBe(["./", "/dev/null", "/tmp"]);
        result.BashPolicy.OkCmds.ShouldContain(new CmdSpec("git status"));
        result.BashPolicy.OkCmds.ShouldContain(new CmdSpec("env", execPos: [0]));
        result.BashPolicy.OkCmds.ShouldContain(new CmdSpec("curl", destFlags: ["-o", "--output"]));

        CmdSpec find = result.BashPolicy.OkCmds.Single(static spec => spec.Equals(new CmdSpec("find")));
        find.Denied.ShouldBe(["-delete", "-ok", "-okdir"]);
        find.ExecFlags.ShouldBe(["-exec", "-execdir"]);
    }

    [Fact]
    public void LoadFromText_DefaultJson_LoadsAuthoritativePythonDefaults()
    {
        ConfigLoader loader = new();

        ShellPolicyPair result = loader.LoadFromText(DefaultConfigs.BashDefaultJson, "config.json");

        result.BashPolicy.OkDests.OrderBy(static dest => dest, StringComparer.Ordinal).ShouldBe(["./", "/dev/null", "/tmp"]);
        result.BashPolicy.OkCmds.ShouldContain(new CmdSpec("tar"));
        result.BashPolicy.OkCmds.ShouldContain(new CmdSpec("["));
        result.PsPolicy.OkCmds.ShouldBeEmpty();
        result.PsPolicy.OkDests.ShouldBeEmpty();
    }
}
