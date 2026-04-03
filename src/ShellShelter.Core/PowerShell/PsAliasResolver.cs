namespace ShellShelter.Core.PowerShell;

/// <summary>
/// Maps well-known PowerShell built-in aliases to their canonical cmdlet names.
/// </summary>
/// <remarks>
/// Aliases are resolved before allowlist validation so the allowlist only needs to
/// list canonical names (e.g. <c>Get-ChildItem</c>, not <c>ls</c> and <c>dir</c>).
/// Only PowerShell built-in aliases are included; user-defined aliases cannot be
/// resolved statically and are passed through unchanged.
/// </remarks>
public static class PsAliasResolver
{
    // Source: https://docs.microsoft.com/powershell/module/microsoft.powershell.core/about/about_aliases
    // and the output of `Get-Alias` on PS 7.4.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // Navigation / location
        { "cd",       "Set-Location" },
        { "chdir",    "Set-Location" },
        { "sl",       "Set-Location" },
        { "pwd",      "Get-Location" },
        { "gl",       "Get-Location" },
        { "pushd",    "Push-Location" },
        { "popd",     "Pop-Location" },

        // Child items
        { "ls",       "Get-ChildItem" },
        { "dir",      "Get-ChildItem" },
        { "gci",      "Get-ChildItem" },

        // Content
        { "cat",      "Get-Content" },
        { "type",     "Get-Content" },
        { "gc",       "Get-Content" },

        // Item management
        { "cp",       "Copy-Item" },
        { "copy",     "Copy-Item" },
        { "cpi",      "Copy-Item" },
        { "mv",       "Move-Item" },
        { "move",     "Move-Item" },
        { "mi",       "Move-Item" },
        { "rm",       "Remove-Item" },
        { "del",      "Remove-Item" },
        { "erase",    "Remove-Item" },
        { "rd",       "Remove-Item" },
        { "ri",       "Remove-Item" },
        { "rmdir",    "Remove-Item" },
        { "md",       "mkdir" },
        { "ni",       "New-Item" },

        // Process
        { "kill",     "Stop-Process" },
        { "spps",     "Stop-Process" },
        { "ps",       "Get-Process" },
        { "gps",      "Get-Process" },

        // Services
        { "gsv",      "Get-Service" },
        { "sasv",     "Start-Service" },
        { "spsv",     "Stop-Service" },

        // Output
        { "echo",     "Write-Output" },
        { "write",    "Write-Output" },

        // History / misc
        { "h",        "Get-History" },
        { "history",  "Get-History" },
        { "r",        "Invoke-History" },
        { "ihy",      "Invoke-History" },

        // Commands / help
        { "gcm",      "Get-Command" },
        { "help",     "Get-Help" },
        { "man",      "help" },

        // Measure / format
        { "measure",  "Measure-Object" },
        { "ft",       "Format-Table" },
        { "fl",       "Format-List" },
        { "fw",       "Format-Wide" },
        { "fhx",      "Format-Hex" },

        // Variables / properties
        { "gv",       "Get-Variable" },
        { "sv",       "Set-Variable" },
        { "rv",       "Remove-Variable" },
        { "clv",      "Clear-Variable" },
        { "select",   "Select-Object" },
        { "where",    "Where-Object" },
        { "?",        "Where-Object" },
        { "%",        "ForEach-Object" },
        { "foreach",  "ForEach-Object" },

        // Sort / group
        { "sort",     "Sort-Object" },
        { "gu",       "Get-Unique" },
        { "group",    "Group-Object" },

        // Output destination
        { "tee",      "Tee-Object" },

        // Clipboard
        { "scb",      "Set-Clipboard" },
        { "gcb",      "Get-Clipboard" },

        // Web
        { "iwr",      "Invoke-WebRequest" },
        { "curl",     "Invoke-WebRequest" },
        { "wget",     "Invoke-WebRequest" },
        { "irm",      "Invoke-RestMethod" },

        // Path
        { "resolve",  "Resolve-Path" },
        { "rvpa",     "Resolve-Path" },

        // Environment
        { "gal",      "Get-Alias" },
        { "nal",      "New-Alias" },
        { "sal",      "Set-Alias" },
        { "epal",     "Export-Alias" },
        { "ipal",     "Import-Alias" },

        // Clear
        { "clear",    "Clear-Host" },
        { "cls",      "Clear-Host" },
        { "clc",      "Clear-Content" },
        { "cli",      "Clear-Item" },

        // Misc
        { "compare",  "Compare-Object" },
        { "diff",     "Compare-Object" },
        { "sls",      "Select-String" },
        { "sleep",    "Start-Sleep" },
        { "start",    "Start-Process" },
        { "ii",       "Invoke-Item" },
        { "ac",       "Add-Content" },
        { "sc",       "Set-Content" },
        { "ogv",      "Out-GridView" },
        { "oh",       "Out-Host" },
        { "toclipboard", "Set-Clipboard" },
        { "fromclipboard", "Get-Clipboard" },
    };

    /// <summary>
    /// Resolves a PowerShell alias to its canonical cmdlet name.
    /// </summary>
    /// <param name="token">The command token (alias or canonical name).</param>
    /// <returns>
    /// The canonical cmdlet name if the token is a known alias; otherwise the original token.
    /// </returns>
    public static string Resolve(string token) =>
        Aliases.TryGetValue(token, out string? canonical) ? canonical : token;

    /// <summary>
    /// Returns whether the given token is a known PowerShell built-in alias.
    /// </summary>
    public static bool IsAlias(string token) => Aliases.ContainsKey(token);
}
