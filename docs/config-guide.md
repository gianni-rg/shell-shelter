# ShellShelter Config Guide

This guide describes how ShellShelter policies are defined and loaded.

## Policy Model

A ShellShelter policy has two parts:

- `ok_cmds`: allowed command specs (`CmdSpec` format)
- `ok_dests`: allowed write destinations (prefix-matched after normalization)

The same model is used for Bash and PowerShell.

## Formats

ShellShelter supports:

- INI (`safecmd`-compatible)
- JSON (native .NET format)

Both map to `ShellPolicy` objects through `ConfigLoader`.

## INI Format

### Sections

- `[DEFAULT]` for Bash policy
- `[POWERSHELL]` for PowerShell policy

If `[POWERSHELL]` is omitted, PowerShell falls back to built-in defaults.

### Minimal Example

```ini
[DEFAULT]
ok_dests = ./, /tmp
ok_cmds = cat, grep, git status

[POWERSHELL]
ok_dests = .\\, $env:TEMP, /tmp
ok_cmds = Get-ChildItem, Get-Content
```

## JSON Format

### Shape

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

## CmdSpec Syntax

A command spec can define denied flags and recursive exec/destination handling.

Base format:

```text
command-prefix[:denied-flags][:exec=...][:dest=...]
```

Examples:

- `git status`
- `find:-delete|-ok|-okdir:exec=-exec|-execdir`
- `tee:dest=$0`
- `cp:dest=$-1`

Notes:

- Prefix matching is token-based (not substring-based).
- `exec` entries indicate arguments that should be recursively validated as commands.
- `dest` entries indicate arguments that should be validated as write destinations.

## Destination Validation

Destinations are normalized before matching:

1. environment expansion
2. user-home expansion (`~` where applicable)
3. absolute path resolution

A destination is allowed only when the normalized target starts with one of the normalized
`ok_dests` prefixes.

## Loading Behavior

`ConfigLoader` resolves format by file extension and content.

Common entry points:

- `ConfigLoader.Load(path)`
- `ConfigLoader.LoadFromText(text)`
- `ConfigLoader.GetDefaultConfigPath()`

If loading fails, higher-level tool APIs fall back to built-in defaults.

## Best Practices

- Keep `ok_cmds` minimal and task-specific.
- Use canonical cmdlet names in PowerShell policy entries.
- Restrict `ok_dests` to workspace-scoped paths whenever possible.
- Avoid broad system prefixes unless explicitly required.
