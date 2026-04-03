# shell-shelter

A tool to white-list shell commands safely. Multi-shell support: PowerShell and Bash

## Prerequisites

| Tool | Required for | Install |
|------|-------------|--------|
| [shfmt](https://github.com/mvdan/sh) | Bash command parsing | `winget install mvdan.shfmt` (Windows), `brew install shfmt` (macOS), `apt-get install shfmt` (Linux) |
| [pwsh](https://github.com/PowerShell/PowerShell) 7+ | PowerShell command execution | `winget install Microsoft.PowerShell` (Windows), `brew install --cask powershell` (macOS), see [aka.ms/install-powershell](https://aka.ms/install-powershell) |

Both are optional at build time - tests that require them are skipped automatically when the tool is not in PATH.
