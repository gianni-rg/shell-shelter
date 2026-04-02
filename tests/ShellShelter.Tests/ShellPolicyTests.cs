using Shouldly;
using ShellShelter.Core;

namespace ShellShelter.Tests;

public sealed class ShellPolicyTests
{
    [Fact]
    public void Constructor_WithValues_DeduplicatesCommandsByNameAndTrimsDestinations()
    {
        ShellPolicy policy = new(
            [new CmdSpec("cat"), new CmdSpec("cat", denied: ["-n"]), new CmdSpec("git status")],
            [" ./ ", "./", null!, "/tmp"]);

        policy.OkCmds.OrderBy(static spec => string.Join(" ", spec.Name), StringComparer.Ordinal)
            .ShouldBe([new CmdSpec("cat"), new CmdSpec("git status")]);
        policy.OkDests.OrderBy(static dest => dest, StringComparer.Ordinal).ShouldBe(["./", "/tmp"]);
    }

    [Fact]
    public void WithAdd_WithValues_ReturnsModifiedCloneWithoutChangingOriginal()
    {
        ShellPolicy policy = new([new CmdSpec("cat")], ["./"]);

        ShellPolicy clone = policy.WithAdd([new CmdSpec("git status")], ["/tmp"]);

        clone.OkCmds.OrderBy(static spec => string.Join(" ", spec.Name), StringComparer.Ordinal)
            .ShouldBe([new CmdSpec("cat"), new CmdSpec("git status")]);
        clone.OkDests.OrderBy(static dest => dest, StringComparer.Ordinal).ShouldBe(["./", "/tmp"]);
        policy.OkCmds.ShouldBe([new CmdSpec("cat")]);
        policy.OkDests.ShouldBe(["./"]);
    }

    [Fact]
    public void WithRemove_WithValues_ReturnsModifiedCloneWithoutChangingOriginal()
    {
        ShellPolicy policy = new([new CmdSpec("cat"), new CmdSpec("git status")], ["./", "/tmp"]);

        ShellPolicy clone = policy.WithRemove([new CmdSpec("git status")], ["/tmp"]);

        clone.OkCmds.ShouldBe([new CmdSpec("cat")]);
        clone.OkDests.ShouldBe(["./"]);
        policy.OkCmds.OrderBy(static spec => string.Join(" ", spec.Name), StringComparer.Ordinal)
            .ShouldBe([new CmdSpec("cat"), new CmdSpec("git status")]);
        policy.OkDests.OrderBy(static dest => dest, StringComparer.Ordinal).ShouldBe(["./", "/tmp"]);
    }
}
