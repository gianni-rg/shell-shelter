using ShellShelter.Core;
using ShellShelter.Core.Bash;
using ShellShelter.Core.Config;
using ShellShelter.Core.PowerShell;

// ---------------------------------------------------------------------------
// ShellShelter CLI — shellshelter <shell> <cmd>
//
// Usage:
//   shellshelter bash <cmd>     — run <cmd> in bash against the allowlist
//   shellshelter pwsh <cmd>     — run <cmd> in pwsh against the allowlist
//   shellshelter config path    — print the active config file path
// ---------------------------------------------------------------------------

if (args.Length < 2)
{
    PrintUsage();
    return 1;
}

string subcommand = args[0].ToLowerInvariant();
string cmd = string.Join(' ', args[1..]);

var loader = new ConfigLoader();

try
{
    switch (subcommand)
    {
        case "bash":
            {
                ShellPolicy policy = LoadBashPolicy(loader);
                string output = await BashShell.SafeRunAsync(cmd, policy);
                Console.Write(output);
                return 0;
            }

        case "pwsh":
            {
                ShellPolicy policy = LoadPsPolicy(loader);
                string output = await PsShell.SafeRunAsync(cmd, policy);
                Console.Write(output);
                return 0;
            }

        case "config" when args.Length >= 2 && args[1].Equals("path", StringComparison.OrdinalIgnoreCase):
            Console.WriteLine(ConfigLoader.GetDefaultConfigPath());
            return 0;

        default:
            Console.Error.WriteLine($"Unknown subcommand '{args[0]}'.");
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

static ShellPolicy LoadBashPolicy(ConfigLoader loader)
{
    string path = ConfigLoader.GetDefaultConfigPath();
    if (File.Exists(path))
    {
        try { return loader.Load(path).BashPolicy; }
        catch { /* fall through to defaults */ }
    }
    return loader.LoadFromText(DefaultConfigs.BashDefaultIni).BashPolicy;
}

static ShellPolicy LoadPsPolicy(ConfigLoader loader)
{
    string path = ConfigLoader.GetDefaultConfigPath();
    if (File.Exists(path))
    {
        try { return loader.Load(path).PsPolicy; }
        catch { /* fall through to defaults */ }
    }
    return loader.LoadFromText(DefaultConfigs.BashDefaultIni + "\n" + DefaultConfigs.PsDefaultIni).PsPolicy;
}

static void PrintUsage()
{
    Console.Error.WriteLine("""
        Usage: shellshelter <subcommand> <args>

        Subcommands:
          bash <cmd>       Run <cmd> in bash against the allowlist
          pwsh <cmd>       Run <cmd> in pwsh against the allowlist
          config path      Print the active config file path
        """);
}
