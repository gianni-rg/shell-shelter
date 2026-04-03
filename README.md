# ShellShelter

ShellShelter is a .NET allowlist engine for shell commands, available as a CLI tool and a C# library.

It provides a safe interface to run shell commands from untrusted sources, such as user input, configuration files, or LLM-generated content, by validating them against a customizable allowlist of safe commands and write destinations. *Extracted commands* and *write destinations* are validated **before** execution, with first-class support for both Bash and PowerShell.

> It is an early-stage experimental project. **It is not yet stable and/or production-ready.** The tool is functional, but it is still under active development, debugging, and testing. Data loss, corruption, or unexpected behaviors may occur. *Use it at your own risk*.

The approach is inspired by [SafeCmd](https://answerdotai.github.io/safecmd/) by Answer.AI, which solves the problem of running shell commands from untrusted sources by validating bash commands against an allowlist before execution. Instead of trying to blacklist dangerous patterns (which is error-prone and easy to bypass), it uses a generous allowlist of read-only and easily-reverted commands that are safe to run.

ShellShelter is a more general and extensible implementation of the same concept, with support for both Bash and PowerShell, a shared policy engine, and a C# API for easy integration in .NET applications. The inherited innovation is that commands are validated using a syntax parser to build an Abstract Syntax Tree (AST) of each command, better handling complex syntax pipelines, command substitutions, sub-commands, sub-shells, and validating every command, even nested ones, before anything executes.

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

## Acknowledgements

Inspired by [Answer.AI's SafeCmd](https://answerdotai.github.io/safecmd/), a helper tool that solves the problem of running shell commands from untrusted sources by validating bash commands against an allowlist before execution.

## Contribution

The project is constantly evolving and contributions are warmly welcomed.

I'm more than happy to receive any kind of contribution to this experimental project: from helpful feedbacks to bug reports, documentation, usage examples, feature requests, or directly code contribution for bug fixes and new and/or improved features.

Feel free to file issues and pull requests on the repository and I'll address them as much as I can, *with a best effort approach during my spare time*. DO NOT expect a super fast turnaround, but I'll do my best to keep the project active and responsive.

> Development is mainly done on Windows, but cross-platform support should be improved. Help improving and validating non-Windows environments is very welcome.

## License

This project is licensed under the [Apache License 2.0](./LICENSE).

Copyright © 2026 Gianni Rosa Gallina.
