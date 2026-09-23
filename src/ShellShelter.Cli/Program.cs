using ShellShelter.Core;
using ShellShelter.Core.Bash;
using ShellShelter.Core.Config;
using ShellShelter.Core.PowerShell;

// ---------------------------------------------------------------------------
// ShellShelter CLI — shellshelter [--config <path>] <shell|config> <cmd|path>
//
// Usage:
//   shellshelter bash <cmd>              — run <cmd> in bash against the allowlist
//   shellshelter pwsh <cmd>              — run <cmd> in pwsh against the allowlist
//   shellshelter config path             — print the active config file path
//   shellshelter --config <path> bash <cmd>
//   shellshelter --config . bash <cmd>   — auto-discover shellshelter.json up the tree
// ---------------------------------------------------------------------------

string? customConfig = null;
int argsStart = 0;

while (argsStart < args.Length)
{
    string argument = args[argsStart];
    if (argument.Equals("--config", StringComparison.OrdinalIgnoreCase) || argument.Equals("-c", StringComparison.OrdinalIgnoreCase))
    {
        if (argsStart + 1 >= args.Length)
        {
            Console.Error.WriteLine("--config requires a path argument.");
            PrintUsage();
            return 1;
        }

        customConfig = args[argsStart + 1];
        argsStart += 2;
        continue;
    }

    break;
}

if (argsStart + 1 >= args.Length)
{
    PrintUsage();
    return 1;
}

string subcommand = args[argsStart].ToLowerInvariant();
string cmd = string.Join(' ', args[(argsStart + 1)..]);

var loader = new ConfigLoader();

// Resolve the config path: custom > auto-discover > global default
string? resolvedConfig = ResolveConfigPath(customConfig);

try
{
    switch (subcommand)
    {
        case "bash":
            {
                ShellPolicy policy = LoadBashPolicy(loader, resolvedConfig);
                var maps = BashShell.BuildExtractionMaps(policy);
                string output = await BashShell.SafeRunAsync(
                    cmd,
                    policy,
                    execFlags: maps.ExecFlags,
                    destFlags: maps.DestFlags,
                    execPos: maps.ExecPos,
                    destPos: maps.DestPos);
                Console.Write(output);
                return 0;
            }

        case "pwsh":
            {
                ShellPolicy policy = LoadPsPolicy(loader, resolvedConfig);
                string output = await PsShell.SafeRunAsync(cmd, policy);
                Console.Write(output);
                return 0;
            }

        case "validate" when argsStart + 2 < args.Length:
            {
                string validateShell = args[argsStart + 1].ToLowerInvariant();
                string validateCmd = string.Join(' ', args[(argsStart + 2)..]);
                switch (validateShell)
                {
                    case "bash":
                        {
                            ShellPolicy policy = LoadBashPolicy(loader, resolvedConfig);
                            var maps = BashShell.BuildExtractionMaps(policy);
                            await BashShell.ValidateAsync(
                                validateCmd,
                                policy,
                                execFlags: maps.ExecFlags,
                                destFlags: maps.DestFlags,
                                execPos: maps.ExecPos,
                                destPos: maps.DestPos);
                            return 0;
                        }

                    case "pwsh":
                        {
                            ShellPolicy policy = LoadPsPolicy(loader, resolvedConfig);
                            await PsShell.ValidateAsync(validateCmd, policy);
                            return 0;
                        }

                    default:
                        Console.Error.WriteLine($"Unknown shell '{validateShell}' for validate. Use 'bash' or 'pwsh'.");
                        PrintUsage();
                        return 1;
                }
            }

        case "validate":
            Console.Error.WriteLine("'validate' requires a shell ('bash'|'pwsh') and a command.");
            PrintUsage();
            return 1;

        case "config" when (argsStart + 1 < args.Length && args[argsStart + 1].Equals("path", StringComparison.OrdinalIgnoreCase)):
            Console.WriteLine(resolvedConfig ?? ConfigLoader.GetDefaultConfigPath());
            return 0;

        case "config":
            Console.Error.WriteLine("'config' requires 'path' subcommand.");
            PrintUsage();
            return 1;

        case "export" when argsStart + 1 < args.Length:
            {
                string format = args[argsStart + 1].ToLowerInvariant();
                string output = format switch
                {
                    "json" => DefaultConfigs.BashDefaultJson,
                    "ini" => DefaultConfigs.BashDefaultIni + Environment.NewLine + DefaultConfigs.PsDefaultIniPlatform,
                    _ => throw new ArgumentException($"Unknown export format: {format}")
                };
                Console.Write(output);
                return 0;
            }

        default:
            Console.Error.WriteLine($"Unknown subcommand '{subcommand}'.");
            PrintUsage();
            return 1;
    }
}
catch (DisallowedCmdException ex)
{
    Console.Error.WriteLine($"Command not allowed: {ex.Message}");
    return 2;
}
catch (DisallowedDestException ex)
{
    Console.Error.WriteLine($"Destination not allowed: {ex.Message}");
    return 2;
}
catch (ShfmtNotFoundException ex)
{
    Console.Error.WriteLine($"shfmt not found: {ex.Message}");
    return 3;
}
catch (PwshNotFoundException ex)
{
    Console.Error.WriteLine($"pwsh not found: {ex.Message}");
    return 3;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static string? ResolveConfigPath(string? customConfig)
{
    if (customConfig == null)
        return null; // Let the policy loader check the global default

    if (customConfig.Equals(".", StringComparison.OrdinalIgnoreCase))
    {
        // Auto-discover: walk up from cwd for shellshelter.json, shellshelter.config.json, .shellshelter
        string currentDir = Directory.GetCurrentDirectory();
        var candidates = new[] { "shellshelter.json", "shellshelter.config.json", ".shellshelter" };

        string? dir = currentDir;
        while (dir != null)
        {
            foreach (var candidate in candidates)
            {
                var path = Path.Combine(dir, candidate);
                if (File.Exists(path))
                    return path;
            }
            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir)
                break;
            dir = parent;
        }
        return null; // Not found; fall back to global default
    }

    // Explicit path — resolve relative to cwd, absolute as-is
    return Path.IsPathRooted(customConfig)
        ? customConfig
        : Path.GetFullPath(customConfig);
}

static ShellPolicy LoadBashPolicy(ConfigLoader loader, string? configPath)
{
    if (configPath != null)
        return loader.Load(configPath).BashPolicy;

    string globalConfigPath = ConfigLoader.GetDefaultConfigPath();
    return loader.LoadOrDefault(globalConfigPath, DefaultConfigs.BashDefaultIni).BashPolicy;
}

static ShellPolicy LoadPsPolicy(ConfigLoader loader, string? configPath)
{
    if (configPath != null)
        return loader.Load(configPath).PsPolicy;

    string globalConfigPath = ConfigLoader.GetDefaultConfigPath();
    return loader.LoadOrDefault(globalConfigPath, DefaultConfigs.BashDefaultIni + "\n" + DefaultConfigs.PsDefaultIniPlatform).PsPolicy;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Usage: shellshelter [options] <subcommand> [args]

        Options:
          --config <path>, -c <path>   Path to a custom allowlist config file
                                       Use '.' to auto-discover from the current directory

        Subcommands:
          bash <cmd>                   Run <cmd> in bash against the allowlist
          pwsh <cmd>                   Run <cmd> in pwsh against the allowlist
          validate bash|pwsh <cmd>     Validate <cmd> against the allowlist without executing it
          config path                  Print the active config file path (respects --config)
          export json|ini              Print the built-in default allowlist as JSON or INI

        Examples:
          shellshelter bash "echo hello"
          shellshelter validate bash "echo hello"
          shellshelter export json > shellshelter.json
          shellshelter -c . pwsh "Get-ChildItem"
        """);
}
