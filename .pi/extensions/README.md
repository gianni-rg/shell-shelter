# ShellShelter Gate Extension

A pi coding agent extension that gates all `bash` and `PowerShell` tool calls through the [ShellShelter](https://github.com/gianni-rg/shell-shelter/) allowlist engine.

When a command is blocked, you can:

- **🚫 Block** — deny the command
- **▶ Execute once** — allow just this time
- **🔓 Allow for this session** — allow until session restart
- **✅ Add to allowlist** — add permanently to `.shellshelter`

## Prerequisites

You need the ShellShelter CLI installed globally:

```bash
dotnet tool install --global ShellShelter.Cli
```

(Or update if already installed: `dotnet tool update --global ShellShelter.Cli`)

## Installation

### Option 1: Project-specific (recommended)

Place these files in your project:

```text
.your-project/
├── .pi/
│   └── extensions/
│       ├── shell-shelter-gate.ts
│       └── README.shell-shelter-gate.md
├── .shellshelter          # Auto-generated on first run (or copy from repo)
└── ...
```

The extension auto-loads when pi starts in that project.

### Option 2: Load directly

```bash
pi --extension path/to/shell-shelter-gate.ts
```

### Option 3: Global installation

Copy to the global extensions folder:

```bash
cp shell-shelter-gate.ts ~/.pi/extensions/
```

## Configuration

The gate reads `.shellshelter` from your project root (auto-discovered). On first run, if no config exists, it exports the default allowlist.

### Default okDests (Windows)

| Shell | Paths |
|-------|-------|
| bash | `./`, `/dev/null`, `%TEMP%`, `C:\Temp` |
| PowerShell | `.\\`, `$env:TEMP` |

### Default okCmds (bash)

Readers: `cat`, `ls`, `grep`, `head`, `tail`, `less`, `bat`, `file`, `stat`, `du`, `df`, `which`, etc.

Writers: `echo`, `printf`, `git add/commit/switch/checkout/fetch`, `npm install`, `yarn install`, `pnpm install`, `bun install`, `docker pull/build`, `curl` (to file), `mkdir`, `cp`, `mv` (dest in okDests)

Self-management: `shellshelter`

### Default okCmds (PowerShell)

Readers: `Get-ChildItem`, `Get-Content`, `Select-String`, `Get-Process`, `Get-Service`, etc.

Writers: `Tee-Object` (dest validated), `git add/commit/switch/checkout/fetch`

Self-management: `dotnet`, `shellshelter`

## Debug

View current gate status:

```shell
pi --command shell-shelter-status
```

Shows: config path, bash okCmds count, PowerShell okCmds count, session allowlist count.

## How it works

1. Every `bash`/`PowerShell` tool call is wrapped through the gate
2. Session allowlist checked first (zero overhead)
3. All other commands go through `shellshelter` CLI for full AST validation
4. Blocked commands prompt the user with 4 options

## Files

- `shell-shelter-gate.ts` — the extension
- `.shellshelter` — project allowlist config (auto-generated or manual)

## License

Copyright (c) 2026 Gianni Rosa Gallina.
Licensed under the Apache 2.0 License.
