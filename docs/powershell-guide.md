# ShellShelter PowerShell Guide

This guide explains ShellShelter PowerShell behavior and policy semantics.

## Overview

PowerShell support is implemented through:

- `PsExtractor`: AST extraction using `System.Management.Automation.Language.Parser`
- `PsAliasResolver`: alias normalization to canonical cmdlet names
- `PsShell` and `PsRunner`: validation plus explicit execution via `pwsh`

PowerShell and Bash both feed the same policy engine.

## Validation Flow

1. Parse command text into PowerShell AST.
2. Extract command tokens, operators, and redirects.
3. Resolve aliases in command position to canonical cmdlet names.
4. Validate commands against `ShellPolicy.OkCmds`.
5. Validate redirect destinations against `ShellPolicy.OkDests`.
6. Execute with `pwsh -NonInteractive -Command <cmd>` only if validation passes.

## Alias Resolution

Allowlist checks are done against canonical names.

Examples:

- `ls` -> `Get-ChildItem`
- `cat` -> `Get-Content`
- `echo` -> `Write-Output`
- `rm` -> `Remove-Item`

Recommendation: configure policy with canonical cmdlet names only.

## Default PowerShell Policy

Built-in defaults include read-oriented cmdlets and selected developer tooling.

Typical cmdlets include:

- `Get-ChildItem`, `Get-Content`, `Select-String`, `Where-Object`, `Sort-Object`
- `Get-Process`, `Get-Service`, `Get-Date`
- selected `git`, `gh`, and `dotnet` command prefixes

Write destinations default to:

- `.\\`
- `$env:TEMP`
- `/tmp`

## Differences from Bash

- Parsing uses in-process AST APIs, not `shfmt`.
- Alias normalization is mandatory before command allowlist checks.
- Execution uses `pwsh` argument list APIs instead of shell string interpolation.

## Security Notes

- Command matching is token-based.
- Redirect destinations are normalized before prefix checks.
- `pwsh` is invoked explicitly (`UseShellExecute=false`).
- Missing `pwsh` triggers first-use install prompt, then immediate throw on repeated absence.

## API Surface

Main PowerShell APIs:

- `PsShell.ValidateAsync(cmd, policy)`
- `PsShell.ExtractAsync(cmd)`
- `PsShell.SafeRunAsync(cmd, policy, pwshPath)`
- `PsTools.Pwsh(...)`
- `PsTools.UnsafePwsh(...)`
- `PsTools.ReadFile(...)`
- `PsTools.ReplaceInFile(...)`

## Troubleshooting

### pwsh not found

Install PowerShell 7+:

- Windows: `winget install -e --id Microsoft.PowerShell`
- macOS: `brew install --cask powershell`
- Linux: follow <https://aka.ms/install-powershell>

### Command denied after alias use

Use canonical cmdlet names in policy and confirm alias mapping is expected.

### Destination denied

Add an appropriate safe prefix to `ok_dests` and retry.
