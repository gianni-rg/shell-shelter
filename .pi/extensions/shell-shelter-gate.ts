/**
 * ShellShelter Gate Extension
 * Copyright (c) 2026 Gianni Rosa Gallina.
 * Licensed under the Apache 2.0 License.
 *
 * Gates all bash and PowerShell tool calls through ShellShelter.
 * When a command is blocked by the allowlist, the user is prompted
 * to: Block, Execute Once, or Add to Allowlist.
 *
 * Config file: .shellshelter (auto-discovered by shellshelter CLI)
 *
 * Usage:
 *   Place this in .pi/extensions/ and it auto-loads for this project.
 *   Or load directly: pi --extension .pi/extensions/shell-shelter-gate.ts
 */

import type { ExtensionAPI, ExtensionContext } from "@earendil-works/pi-coding-agent";
import { createBashTool, createPowerShellTool } from "@earendil-works/pi-coding-agent";
import * as fs from "fs";
import * as path from "path";

// ---------------------------------------------------------------------------
// Config
// ---------------------------------------------------------------------------

const CONFIG_FILENAME = ".shellshelter";
let configPath: string | undefined;

/** Session-scoped allowlist — persisted for the lifetime of this pi session only. */
const sessionAllowlist = new Set<string>();

/** Check if a command is in the session allowlist. */
function isSessionAllowed(cmd: string, shell: "bash" | "powershell"): boolean {
  return sessionAllowlist.has(`${shell}:${cmd}`);
}

/** Add a command to the session allowlist. */
function addSessionAllow(cmd: string, shell: "bash" | "powershell"): void {
  sessionAllowlist.add(`${shell}:${cmd}`);
}

function resolveConfigPath(repoRoot: string): string | undefined {
  // Mirror the CLI's `--config .` auto-discovery (Program.cs ResolveConfigPath): search these
  // filenames, walking up parent directories, before falling back to creating a new config.
  const candidates = ["shellshelter.json", "shellshelter.config.json", CONFIG_FILENAME];
  let dir: string | undefined = repoRoot;
  while (dir) {
    for (const candidateName of candidates) {
      const candidate = path.join(dir, candidateName);
      if (fs.existsSync(candidate)) return candidate;
    }
    const parent = path.dirname(dir);
    if (parent === dir) break;
    dir = parent;
  }
  return undefined;
}

interface ShellShelterConfig {
  bash?: { okDests?: string[]; okCmds?: string[] };
  powershell?: { okDests?: string[]; okCmds?: string[] };
}

/** ShellShelter also supports INI-format `.shellshelter` files, which this extension cannot safely rewrite. */
function isJsonConfigFile(filePath: string): boolean {
  if (filePath.toLowerCase().endsWith(".json")) return true;
  if (!fs.existsSync(filePath)) return true;
  try {
    return fs.readFileSync(filePath, "utf-8").trim().startsWith("{");
  } catch {
    return true;
  }
}

function loadConfig(): ShellShelterConfig | null {
  if (!configPath || !fs.existsSync(configPath) || !isJsonConfigFile(configPath)) return null;
  try {
    return JSON.parse(fs.readFileSync(configPath, "utf-8")) as ShellShelterConfig;
  } catch {
    return null;
  }
}

function saveConfig(config: ShellShelterConfig): void {
  if (!configPath) return;
  fs.writeFileSync(configPath, JSON.stringify(config, null, 2) + "\n");
}

/**
 * `CmdSpec.FromStr` splits on `:` to parse `denied`/`exec=`/`dest=` metadata, so a raw command
 * containing a colon (e.g. PowerShell `-FilePath:C:\foo`) would be silently reinterpreted —
 * potentially narrowing the allowed name prefix or turning part of the command into a denied
 * flag. There is no escaping for this in the CmdSpec serialization, so such commands cannot be
 * safely auto-added and must be authored manually.
 */
function isSafeToAutoAdd(cmd: string): boolean {
  return !cmd.includes(":");
}

/** Returns false (and leaves the file untouched) when the config is INI-formatted, unparsable, or unavailable. */
function addCommandToConfig(key: "bash" | "powershell", cmdSpec: string): boolean {
  if (!isSafeToAutoAdd(cmdSpec)) {
    return false;
  }

  if (!configPath || (fs.existsSync(configPath) && !isJsonConfigFile(configPath))) {
    return false;
  }

  // Only synthesize a fresh empty config when no file exists yet; if one exists but fails to
  // parse (e.g. rejected by JSON.parse), refuse rather than silently erasing it.
  const fileExists = fs.existsSync(configPath);
  const config = fileExists
    ? loadConfig()
    : { bash: { okDests: [], okCmds: [] }, powershell: { okDests: [], okCmds: [] } };
  if (!config) return false;

  if (!config[key]) config[key] = { okDests: [], okCmds: [] };
  if (!config[key].okCmds) config[key].okCmds = [];
  // Avoid duplicates
  if (!config[key].okCmds!.includes(cmdSpec)) {
    config[key].okCmds!.push(cmdSpec);
    saveConfig(config);
  }
  return true;
}

// ---------------------------------------------------------------------------
// Command extraction helpers
// ---------------------------------------------------------------------------

/** Extract the command prefix (first 1-2 tokens) from a command string. */
function extractCommandPrefix(cmd: string): string {
  const tokens = cmd.trim().split(/\s+/);
  // Try 2-token prefix first (e.g., "dotnet build"), fall back to 1 token
  if (tokens.length >= 2) {
    return `${tokens[0]} ${tokens[1]}`;
  }
  return tokens[0] ?? cmd;
}

// ---------------------------------------------------------------------------
// CLI integration
// ---------------------------------------------------------------------------

interface ExecResult { ok: boolean; stdout: string; stderr: string; code: number | null; }

/** Map our internal shell name to the shellshelter CLI subcommand. */
function cliShell(shell: "bash" | "powershell"): string {
  return shell === "powershell" ? "pwsh" : "bash";
}

async function shellShelterCheck(
  cmd: string,
  shell: "bash" | "powershell",
  configPath: string
): Promise<ExecResult> {
  const { execFile } = await import("node:child_process");
  const { promisify } = await import("node:util");
  const execFileAsync = promisify(execFile);

  // Args passed as an array (no shell) so command text can never be reinterpreted as shell syntax.
  const args = ["--config", configPath, "validate", cliShell(shell), cmd];

  try {
    const { stdout } = await execFileAsync("shellshelter", args, { timeout: 10000 });
    return { ok: true, stdout, stderr: "", code: 0 };
  } catch (err: any) {
    const code = typeof err.code === "number" ? err.code : (err.exitCode ?? 1);
    const stderr = err.stderr ?? err.message ?? "";
    // Only a clean exit 0 counts as allowed; denials (2), missing tools, timeouts, etc. all fail closed.
    return { ok: false, stdout: "", stderr, code };
  }
}

// ---------------------------------------------------------------------------
// UI prompt
// ---------------------------------------------------------------------------

type GateChoice = "block" | "allow_once" | "allow_session" | "add_to_allowlist";

async function promptGateChoice(
  cmd: string,
  shell: "bash" | "powershell",
  ctx: ExtensionContext
): Promise<GateChoice> {
  if (!ctx.hasUI || !ctx.ui) return "block"; // Fail closed when no UI to prompt

  const cmdShort = cmd.length > 80 ? cmd.slice(0, 80) + "…" : cmd;
  const choice = await ctx.ui.select(
    `ShellShelter: command blocked`,
    [
      `🚫 Block "${cmdShort}"`,
      `▶ Execute once`,
      `🔓 Allow for this session`,
      `✅ Add to allowlist`,
    ]
  );
  switch (choice) {
    case "🚫 Block": return "block";
    case "▶ Execute once": return "allow_once";
    case "🔓 Allow for this session": return "allow_session";
    case "✅ Add to allowlist": return "add_to_allowlist";
    default: return "block";
  }
}

// ---------------------------------------------------------------------------
// Core gate logic
// ---------------------------------------------------------------------------

async function gateCommand(
  cmd: string,
  shell: "bash" | "powershell",
  ctx: ExtensionContext
): Promise<{ blocked: boolean; reason: string }> {
  // Session allowlist check (highest priority — per-session, no CLI overhead)
  if (isSessionAllowed(cmd, shell)) {
    return { blocked: false, reason: "" };
  }

  // Auto-discover from repo root when no project config was resolved yet — never skip validation.
  const configPathForCli = configPath ?? ".";
  const result = await shellShelterCheck(cmd, shell, configPathForCli);

  if (result.ok) {
    return { blocked: false, reason: "" };
  }

  // Only exit code 2 is a policy denial eligible for override; anything else (missing CLI,
  // missing shfmt/pwsh, timeout, malformed config, ...) is an infrastructure failure and stays blocked.
  if (result.code !== 2) {
    return { blocked: true, reason: result.stderr || "ShellShelter validation unavailable" };
  }

  const choice = await promptGateChoice(cmd, shell, ctx);
  switch (choice) {
    case "allow_session": {
      addSessionAllow(cmd, shell);
      ctx.ui?.notify(`Allowed "${extractCommandPrefix(cmd)}" for this session`, "info");
      break;
    }
    case "add_to_allowlist": {
      // Persist the exact command, not just its prefix — CmdSpec treats the name as an
      // allowlisted prefix, so a truncated prefix (e.g. "rm -rf") would permit arbitrary
      // trailing arguments/destinations on every future invocation. Commands containing ':'
      // are rejected by addCommandToConfig — see isSafeToAutoAdd.
      const added = addCommandToConfig(shell, cmd);
      if (added) {
        ctx.ui?.notify(
          `Added to .shellshelter: ${cmd}\n` +
          `Note: this still allows any additional trailing arguments after this exact prefix.`,
          "info"
        );
      } else {
        ctx.ui?.notify(
          `Cannot auto-add "${cmd}": either it contains ':' (which ShellShelter's CmdSpec format ` +
          `reserves for exec=/dest=/denied-flag metadata) or ${configPath ?? ".shellshelter"} could ` +
          `not be safely updated (non-JSON or unparsable). Edit the config manually to add it.`,
          "warning"
        );
        // The command was never persisted — fail closed rather than silently executing it.
        return { blocked: true, reason: "Could not add command to allowlist" };
      }
      break;
    }
    case "allow_once":
      break;
    case "block":
    default:
      return { blocked: true, reason: result.stderr || "Command not in allowlist" };
  }

  return { blocked: false, reason: "" };
}

// ---------------------------------------------------------------------------
// Extension factory
// ---------------------------------------------------------------------------

export default function (pi: ExtensionAPI) {
  const repoRoot = process.cwd();
  configPath = resolveConfigPath(repoRoot);

  // Reset session allowlist on each new session
  pi.on("session_start", async (_event, _ctx) => {
    sessionAllowlist.clear();
  });

  // Ensure config exists on first session
  pi.on("session_start", async (_event, ctx) => {
    if (!ctx.hasUI) return;

    if (!configPath) {
      try {
        const { execSync } = await import("node:child_process");
        const globalConfigPath = execSync("shellshelter config path").toString().trim();
        if (fs.existsSync(globalConfigPath)) {
          // A global config already exists — don't shadow the user's restrictions with a
          // fresh project-local default; let validation fall back to the global config.
          ctx.ui?.notify(`Using existing global ShellShelter config: ${globalConfigPath}`, "info");
        } else {
          const stdout = execSync("shellshelter export json").toString();
          configPath = path.join(repoRoot, CONFIG_FILENAME);
          fs.writeFileSync(configPath, stdout);
          ctx.ui?.notify(`Created .shellshelter with default allowlist`, "info");
        }
      } catch {
        ctx.ui?.notify(
          "ShellShelter CLI not available — commands will be blocked until it is installed. " +
          "Install it with: dotnet tool install --global ShellShelter.Cli",
          "warning"
        );
      }
    }
  });

  // -----------------------------------------------------------------------
  // Wrap bash tool
  // -----------------------------------------------------------------------
  const bashTool = createBashTool(repoRoot);

  pi.registerTool({
    ...bashTool,
    execute: async (id, params, signal, onUpdate, ctx) => {
      // No bypass: gateCommand always validates, auto-discovering/falling back to the global config.
      const cmd = params.command;
      const gate = await gateCommand(cmd, "bash", ctx);
      if (gate.blocked) {
        return {
          content: [{ type: "text", text: `ShellShelter: ${gate.reason}` }],
          details: { tool: "bash", blocked: true, reason: gate.reason },
        };
      }

      return bashTool.execute(id, params, signal, onUpdate);
    },
  });

  // -----------------------------------------------------------------------
  // Wrap PowerShell tool
  // -----------------------------------------------------------------------
  const psTool = createPowerShellTool(repoRoot);

  pi.registerTool({
    ...psTool,
    execute: async (id, params, signal, onUpdate, ctx) => {
      // No bypass: gateCommand always validates, auto-discovering/falling back to the global config.
      const cmd = params.command;
      const gate = await gateCommand(cmd, "powershell", ctx);
      if (gate.blocked) {
        return {
          content: [{ type: "text", text: `ShellShelter: ${gate.reason}` }],
          details: { tool: "powershell", blocked: true, reason: gate.reason },
        };
      }

      return psTool.execute(id, params, signal, onUpdate);
    },
  });

  // -----------------------------------------------------------------------
  // Debug command
  // -----------------------------------------------------------------------
  pi.registerCommand("shell-shelter-status", {
    description: "Show ShellShelter gate status and current allowlist",
    handler: async (_args, ctx) => {
      const config = loadConfig();
      if (!configPath) {
        ctx.ui?.notify("No .shellshelter config found — validation falls back to the global config", "info");
        return;
      }
      const bashCount = config?.bash?.okCmds?.length ?? 0;
      const psCount = config?.powershell?.okCmds?.length ?? 0;
      const sessionCount = sessionAllowlist.size;
      ctx.ui?.notify(
        `ShellShelter gate active\nConfig: ${configPath}\nBash: ${bashCount} commands\nPowerShell: ${psCount} commands\nSession allowlist: ${sessionCount} commands`,
        "info"
      );
    },
  });
}
