using Shouldly;
using ShellShelter.Core.Bash;
using ShellShelter.Core.PowerShell;

namespace ShellShelter.Tests;

public sealed class RunnerHardeningTests
{
    [Fact]
    public void PsRunner_CommandStartInfo_UsesNoProfileAndNonInteractive()
    {
        var psi = PsRunner.CreateCommandStartInfo("Get-Content -Path ./file.txt", "pwsh");

        psi.ArgumentList.Count.ShouldBe(4);
        psi.ArgumentList[0].ShouldBe("-NoProfile");
        psi.ArgumentList[1].ShouldBe("-NonInteractive");
        psi.ArgumentList[2].ShouldBe("-Command");
        psi.ArgumentList[3].ShouldBe("Get-Content -Path ./file.txt");
    }

    [Fact]
    public void PsRunner_VersionProbeStartInfo_UsesNoProfile()
    {
        var psi = PsRunner.CreateVersionProbeStartInfo("pwsh");

        psi.ArgumentList.Count.ShouldBe(4);
        psi.ArgumentList[0].ShouldBe("-NoProfile");
        psi.ArgumentList[1].ShouldBe("-NonInteractive");
        psi.ArgumentList[2].ShouldBe("-Command");
        psi.ArgumentList[3].ShouldBe("$PSVersionTable.PSVersion.Major");
    }

    [Fact]
    public void BashRunner_CommandStartInfo_SanitizesStartupEnv()
    {
        var psi = BashRunner.CreateBashCommandStartInfo("echo hi", "bash");

        psi.Environment["BASH_ENV"].ShouldBe(string.Empty);
        psi.Environment["ENV"].ShouldBe(string.Empty);
        psi.ArgumentList.Count.ShouldBe(2);
        psi.ArgumentList[0].ShouldBe("-c");
        psi.ArgumentList[1].ShouldBe("echo hi");
    }
}
