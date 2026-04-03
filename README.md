# shell-shelter

ShellShelter is a .NET allowlist engine for shell commands.

It validates extracted commands and write destinations before execution, with first-class support
for both Bash and PowerShell.

## Features

- Bash extraction and validation through `shfmt --to-json`
- PowerShell extraction and validation through the PowerShell AST parser
- Shared policy engine (`CmdSpec` and destination allowlists) across both shells
- LLM-friendly tool surface in `ShellShelter.Core.Tools`
- CLI runner for quick local use

## Prerequisites

| Tool | Required for | Install |
| --- | --- | --- |
| [shfmt](https://github.com/mvdan/sh) | Bash command parsing | `winget install mvdan.shfmt` (Windows), `brew install shfmt` (macOS), `apt-get install shfmt` (Linux) |
| [pwsh](https://github.com/PowerShell/PowerShell) 7+ | PowerShell command execution | `winget install Microsoft.PowerShell` (Windows), `brew install --cask powershell` (macOS), see [aka.ms/install-powershell](https://aka.ms/install-powershell) |

Both tools are optional at build time. Tests requiring them are skipped when unavailable.

On first runtime absence of `shfmt` or `pwsh`, ShellShelter prompts to attempt auto-install.
If the tool is still unavailable on a later call, ShellShelter throws immediately.

## Build

```bash
dotnet build ShellShelter.sln
```

## Quick Start

### CLI

```bash
dotnet run --project src/ShellShelter.Cli -- bash "echo hello"
dotnet run --project src/ShellShelter.Cli -- pwsh "Get-ChildItem"
dotnet run --project src/ShellShelter.Cli -- config path
```

### C# API

```csharp
using ShellShelter.Core;
using ShellShelter.Core.Bash;
using ShellShelter.Core.PowerShell;

var bashPolicy = new ShellPolicy(
    okCmds: [new CmdSpec("echo"), new CmdSpec("cat")],
    okDests: ["./", "/tmp"]);

string bashOutput = await BashShell.SafeRunAsync("echo hello", bashPolicy);

var psPolicy = new ShellPolicy(
    okCmds: [new CmdSpec("Get-ChildItem"), new CmdSpec("Get-Content")],
    okDests: [".\\", "$env:TEMP", "/tmp"]);

string psOutput = await PsShell.SafeRunAsync("Get-ChildItem", psPolicy);
```

## Config Format

ShellShelter supports two config formats:

- INI for safecmd compatibility
- JSON as the native .NET format

The default config path is provided by `ConfigLoader.GetDefaultConfigPath()` and supports both
Bash and PowerShell policies.

Example INI:

```ini
[DEFAULT]
ok_dests = ./, /tmp
ok_cmds = cat, grep, git status

[POWERSHELL]
ok_dests = .\\, $env:TEMP, /tmp
ok_cmds = Get-ChildItem, Get-Content
```

Example JSON:

```json
{
    "bash": {
        "okDests": ["./", "/tmp"],
        "okCmds": ["cat", "grep", "git status"]
    },
    "powershell": {
        "okDests": [".\\", "$env:TEMP", "/tmp"],
        "okCmds": ["Get-ChildItem", "Get-Content"]
    }
}
```

See detailed docs:

- [docs/config-guide.md](docs/config-guide.md)
- [docs/powershell-guide.md](docs/powershell-guide.md)
