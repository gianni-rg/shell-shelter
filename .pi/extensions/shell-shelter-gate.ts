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

import type { ExtensionAPI, ExtensionUIContext } from "@earendil-works/pi-coding-agent";
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
  const candidate = path.join(repoRoot, CONFIG_FILENAME);
  return fs.existsSync(candidate) ? candidate : undefined;
}

interface ShellShelterConfig {
  bash?: { okDests?: string[]; okCmds?: string[] };
  powershell?: { okDests?: string[]; okCmds?: string[] };
}

function loadConfig(): ShellShelterConfig | null {
  if (!configPath || !fs.existsSync(configPath)) return null;
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

function addCommandToConfig(key: "bash" | "powershell", cmdSpec: string): void {
  const config = loadConfig() ?? { bash: { okDests: [], okCmds: [] }, powershell: { okDests: [], okCmds: [] } };
  if (!config[key]) config[key] = { okDests: [], okCmds: [] };
  if (!config[key].okCmds) config[key].okCmds = [];
  // Avoid duplicates
  if (!config[key].okCmds!.includes(cmdSpec)) {
    config[key].okCmds!.push(cmdSpec);
    saveConfig(config);
  }
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
  const { exec } = await import("node:child_process");
  const { promisify } = await import("node:util");
  const execAsync = promisify(exec);

  try {
    const { stdout, stderr } = await execAsync(
      `shellshelter --config "${configPath}" validate ${cliShell(shell)} "${cmd.replace(/"/g, '\\"')}"`,
      { timeout: 10000 }
    );
    return { ok: true, stdout, stderr: "", code: 0 };
  } catch (err: any) {
    const code = err.code ?? err.exitCode ?? 1;
    const stderr = err.stderr ?? err.message ?? "";
    // Exit code 2 = command denied by allowlist
    return { ok: code !== 2, stdout: "", stderr, code };
  }
}

// ---------------------------------------------------------------------------
// UI prompt
// ---------------------------------------------------------------------------

type GateChoice = "block" | "allow_once" | "allow_session" | "add_to_allowlist";

async function promptGateChoice(
  cmd: string,
  shell: "bash" | "powershell",
  ctx: ExtensionUIContext
): Promise<GateChoice> {
  if (!ctx.hasUI) return "allow_once"; // Fail open when no UI

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
  ctx: ExtensionUIContext
): Promise<{ blocked: boolean; reason: string }> {
  // Session allowlist check (highest priority — per-session, no CLI overhead)
  if (isSessionAllowed(cmd, shell)) {
    return { blocked: false, reason: "" };
  }

  // All commands go through CLI for full AST / CmdSpec-aware validation
  const configPathForCli = configPath ?? process.cwd();
  const result = await shellShelterCheck(cmd, shell, configPathForCli);

  if (result.ok) {
    return { blocked: false, reason: "" };
  }

  // Command denied — prompt user
  const choice = await promptGateChoice(cmd, shell, ctx);
  switch (choice) {
    case "allow_session": {
      addSessionAllow(cmd, shell);
      ctx.ui.notify(`Allowed "${extractCommandPrefix(cmd)}" for this session`, "success");
      break;
    }
    case "add_to_allowlist": {
      addCommandToConfig(shell, extractCommandPrefix(cmd));
      ctx.ui.notify(`Added to .shellshelter: ${extractCommandPrefix(cmd)}`, "success");
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
        const stdout = execSync("shellshelter export json").toString();
        configPath = path.join(repoRoot, CONFIG_FILENAME);
        fs.writeFileSync(configPath, stdout);
        ctx.ui.notify(`Created .shellshelter with default allowlist`, "success");
      } catch {
        ctx.ui.notify(
          "ShellShelter CLI not available — gating disabled. " +
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
      if (!configPath) {
        return bashTool.execute(id, params, signal, onUpdate);
      }

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
      if (!configPath) {
        return psTool.execute(id, params, signal, onUpdate);
      }

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
        ctx.ui.notify("No .shellshelter config found", "info");
        return;
      }
      const bashCount = config.bash?.okCmds?.length ?? 0;
      const psCount = config.powershell?.okCmds?.length ?? 0;
      const sessionCount = sessionAllowlist.size;
      ctx.ui.notify(
        `ShellShelter gate active\nConfig: ${configPath}\nBash: ${bashCount} commands\nPowerShell: ${psCount} commands\nSession allowlist: ${sessionCount} commands`,
        "info"
      );
    },
  });
}
