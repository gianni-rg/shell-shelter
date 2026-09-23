using System.Text.Json;

namespace ShellShelter.Core.Config;

/// <summary>
/// Provides the built-in bash defaults ported from Python safecmd.
/// </summary>
public static class DefaultConfigs
{
    private static readonly string[] BashDefaultOkDests = OperatingSystem.IsWindows()
        ? [
            "./",
            "/dev/null",
            "%TEMP%",
            "C:\\Temp",
        ]
        : [
            "./",
            "/dev/null",
            "/tmp",
        ];

    private static readonly string[] BashDefaultOkCmds =
    [
        "cat",
        "head",
        "tail",
        "less",
        "more",
        "bat",
        "ls",
        "tree",
        "locate",
        "grep",
        "rg",
        "ag",
        "ack",
        "fgrep",
        "egrep",
        "cut",
        "sort",
        "uniq",
        "wc",
        "tr",
        "column",
        "file",
        "stat",
        "du",
        "df",
        "which",
        "whereis",
        "type",
        "diff",
        "cmp",
        "comm",
        "unzip",
        "gunzip",
        "bunzip2",
        "unrar",
        "ping",
        "dig",
        "nslookup",
        "host",
        "date",
        "cal",
        "uptime",
        "whoami",
        "hostname",
        "uname",
        "printenv",
        "echo",
        "printf",
        "yes",
        "seq",
        "basename",
        "dirname",
        "realpath",
        "git blame",
        "git branch",
        "git cat-file",
        "git config --get",
        "git config --list",
        "git describe",
        "git diff",
        "git log",
        "git ls-files",
        "git ls-tree",
        "git merge-base",
        "git remote",
        "git rev-parse",
        "git shortlog",
        "git show",
        "git stash list",
        "git status",
        "git tag",
        "git fetch",
        "git add",
        "git commit",
        "git switch",
        "git checkout",
        "gh repo view",
        "gh issue list",
        "gh issue view",
        "gh pr list",
        "gh pr view",
        "gh pr status",
        "gh pr checks",
        "gh pr diff",
        "gh release list",
        "gh release view",
        "gh run list",
        "gh run view",
        "gh workflow list",
        "gh workflow view",
        "gh auth status",
        "gh gist list",
        "gh gist view",
        "gh browse",
        "gh search",
        "nbdev_export",
        "nbdev_clean",
        "npm list",
        "npm ls",
        "npm outdated",
        "npm view",
        "npm info",
        "npm why",
        "npm audit",
        "npm config list",
        "npm config get",
        "npm search",
        "npm pack",
        "yarn list",
        "yarn outdated",
        "yarn why",
        "yarn info",
        "yarn config list",
        "yarn config get",
        "pnpm list",
        "pnpm ls",
        "pnpm outdated",
        "pnpm why",
        "pnpm config list",
        "pnpm config get",
        "bun pm ls",
        "bun pm hash",
        "npm install",
        "yarn install",
        "pnpm install",
        "bun install",
        "bat",
        "eza",
        "exa",
        "fd",
        "fzf",
        "dust",
        "duf",
        "tldr",
        "zoxide",
        "httpie",
        "http",
        "jq",
        "yq",
        "docker ps",
        "docker images",
        "docker logs",
        "docker inspect",
        "docker stats",
        "docker top",
        "docker diff",
        "docker history",
        "docker version",
        "docker info",
        "docker pull",
        "docker build",
        "aws s3 ls",
        "aws s3 cp",
        "aws sts get-caller-identity",
        "aws iam get-user",
        "aws iam list-users",
        "aws ec2 describe-instances",
        "aws ec2 describe-vpcs",
        "aws ec2 describe-security-groups",
        "aws logs describe-log-groups",
        "aws logs filter-log-events",
        "aws logs get-log-events",
        "aws lambda list-functions",
        "aws lambda get-function",
        "aws cloudformation describe-stacks",
        "aws cloudformation list-stacks",
        "aws rds describe-db-instances",
        "aws dynamodb list-tables",
        "aws dynamodb describe-table",
        "aws sqs list-queues",
        "aws sns list-topics",
        "aws configure list",
        "aws configure get",
        "gcloud config list",
        "gcloud config get-value",
        "gcloud auth list",
        "gcloud projects list",
        "gcloud projects describe",
        "gcloud compute instances list",
        "gcloud compute instances describe",
        "gcloud compute zones list",
        "gcloud compute regions list",
        "gcloud container clusters list",
        "gcloud container clusters describe",
        "gcloud functions list",
        "gcloud functions describe",
        "gcloud functions logs read",
        "gcloud run services list",
        "gcloud run services describe",
        "gcloud sql instances list",
        "gcloud sql instances describe",
        "gcloud storage ls",
        "gcloud storage cat",
        "gcloud logging read",
        "folder2ctx",
        "repo2ctx",
        "env:exec=$0",
        "xargs:exec=$0",
        "tee:dest=$0",
        "ex:dest=$2",
        "cp:dest=$-1",
        "mv:dest=$-1",
        "mkdir:dest=$-1",
        "find:-delete|-ok|-okdir:exec=-exec|-execdir",
        "rg:--pre",
        "tar:--use-compress-program|--transform|--checkpoint-action|--info-script|--new-volume-script:exec=--to-command|-I",
        "curl:dest=-o|--output",
        "cd",
        "pwd",
        "export",
        "test",
        "[",
        "true",
        "false",
    ];

    /// <summary>
    /// Gets the authoritative Python safecmd bash default INI configuration.
    /// </summary>
    public static string BashDefaultIni => OperatingSystem.IsWindows()
        ? "[DEFAULT]\nok_dests = ./, /dev/null, %TEMP%, C:\\Temp" +
          "\n\nok_cmds = cat, head, tail, less, more, bat"
            + "\n    # Directory listing\n    ls, tree, locate"
            + "\n    # Search\n    grep, rg, ag, ack, fgrep, egrep"
            + "\n    # Text processing\n    cut, sort, uniq, wc, tr, column"
            + "\n    # File info\n    file, stat, du, df, which, whereis, type"
            + "\n    # Comparison\n    diff, cmp, comm"
            + "\n    # Archives\n    unzip, gunzip, bunzip2, unrar"
            + "\n    # Network\n    ping, dig, nslookup, host"
            + "\n    # System info\n    date, cal, uptime, whoami, hostname, uname, printenv"
            + "\n    # Utilities\n    echo, printf, yes, seq, basename, dirname, realpath"
            + "\n    # Git (read-only)\n    git blame, git branch, git cat-file, git config --get, git config --list,"
            + "\n    git describe, git diff, git log, git ls-files, git ls-tree, git merge-base,"
            + "\n    git remote, git rev-parse, git shortlog, git show, git stash list, git status, git tag"
            + "\n    # Git (workspace)\n    git fetch, git add, git commit, git switch, git checkout"
            + "\n    # gh\n    gh repo view, gh issue list, gh issue view, gh pr list, gh pr view, gh pr status, gh pr checks, gh pr diff"
            + "\n    gh release list, gh release view, gh run list, gh run view, gh workflow list, gh workflow view"
            + "\n    gh auth status, gh gist list, gh gist view, gh browse, gh search"
            + "\n    # nbdev\n    nbdev_export, nbdev_clean"
            + "\n    # npm (read-only)\n    npm list, npm ls, npm outdated, npm view, npm info, npm why, npm audit, npm config list, npm config get, npm search, npm pack"
            + "\n    # yarn (read-only)\n    yarn list, yarn outdated, yarn why, yarn info, yarn config list, yarn config get"
            + "\n    # pnpm (read-only)\n    pnpm list, pnpm ls, pnpm outdated, pnpm why, pnpm config list, pnpm config get"
            + "\n    # bun (read-only)\n    bun pm ls, bun pm hash"
            + "\n    # js install\n    npm install, yarn install, pnpm install, bun install"
            + "\n    # Modern Unix (read-only)\n    bat, eza, exa, fd, fzf, dust, duf, tldr, zoxide, httpie, http, jq, yq"
            + "\n    # Docker (read-only)\n    docker ps, docker images, docker logs, docker inspect, docker stats, docker top, docker diff, docker history, docker version, docker info"
            + "\n    # Docker (workspace - reversible)\n    docker pull, docker build"
            + "\n    # AWS (read-only)\n    aws s3 ls, aws s3 cp, aws sts get-caller-identity, aws iam get-user, aws iam list-users"
            + "\n    aws ec2 describe-instances, aws ec2 describe-security-groups\n    aws ec2 describe-vpcs"
            + "\n    aws logs describe-log-groups, aws logs filter-log-events, aws logs get-log-events"
            + "\n    aws lambda list-functions, aws lambda get-function"
            + "\n    aws cloudformation describe-stacks, aws cloudformation list-stacks"
            + "\n    aws rds describe-db-instances, aws dynamodb list-tables, aws dynamodb describe-table"
            + "\n    aws sqs list-queues, aws sns list-topics"
            + "\n    aws configure list, aws configure get"
            + "\n    # GCloud (read-only)\n    gcloud config list, gcloud config get-value, gcloud auth list"
            + "\n    gcloud projects list, gcloud projects describe"
            + "\n    gcloud compute instances list, gcloud compute instances describe, gcloud compute zones list, gcloud compute regions list"
            + "\n    gcloud container clusters list, gcloud container clusters describe"
            + "\n    gcloud functions list, gcloud functions describe, gcloud functions logs read"
            + "\n    gcloud run services list, gcloud run services describe"
            + "\n    gcloud sql instances list, gcloud sql instances describe"
            + "\n    gcloud storage ls, gcloud storage cat"
            + "\n    gcloud logging read"
            + "\n    # toolslm\n            folder2ctx, repo2ctx`n            # Self-management`n            shellshelter"
            + "\n    # Positional exec/dest handling\n    env:exec=$0, xargs:exec=$0"
            + "\n    tee:dest=$0, ex:dest=$2, cp:dest=$-1, mv:dest=$-1, mkdir:dest=$-1"
            + "\n    # Exec/dest flag handling\n    find:-delete|-ok|-okdir:exec=-exec|-execdir"
            + "\n    rg:--pre"
            + "\n    tar:--use-compress-program|--transform|--checkpoint-action|--info-script|--new-volume-script:exec=--to-command|-I"
            + "\n    curl:dest=-o|--output"
            + "\n    # Builtins\n    cd, pwd, export, test, [, true, false"
        : "[DEFAULT]\nok_dests = ./, /dev/null, /tmp" +
          "\n\nok_cmds = cat, head, tail, less, more, bat"
            + "\n    # Directory listing\n    ls, tree, locate"
            + "\n    # Search\n    grep, rg, ag, ack, fgrep, egrep"
            + "\n    # Text processing\n    cut, sort, uniq, wc, tr, column"
            + "\n    # File info\n    file, stat, du, df, which, whereis, type"
            + "\n    # Comparison\n    diff, cmp, comm"
            + "\n    # Archives\n    unzip, gunzip, bunzip2, unrar"
            + "\n    # Network\n    ping, dig, nslookup, host"
            + "\n    # System info\n    date, cal, uptime, whoami, hostname, uname, printenv"
            + "\n    # Utilities\n    echo, printf, yes, seq, basename, dirname, realpath"
            + "\n    # Git (read-only)\n    git blame, git branch, git cat-file, git config --get, git config --list,"
            + "\n    git describe, git diff, git log, git ls-files, git ls-tree, git merge-base,"
            + "\n    git remote, git rev-parse, git shortlog, git show, git stash list, git status, git tag"
            + "\n    # Git (workspace)\n    git fetch, git add, git commit, git switch, git checkout"
            + "\n    # gh\n    gh repo view, gh issue list, gh issue view, gh pr list, gh pr view, gh pr status, gh pr checks, gh pr diff"
            + "\n    gh release list, gh release view, gh run list, gh run view, gh workflow list, gh workflow view"
            + "\n    gh auth status, gh gist list, gh gist view, gh browse, gh search"
            + "\n    # nbdev\n    nbdev_export, nbdev_clean"
            + "\n    # npm (read-only)\n    npm list, npm ls, npm outdated, npm view, npm info, npm why, npm audit, npm config list, npm config get, npm search, npm pack"
            + "\n    # yarn (read-only)\n    yarn list, yarn outdated, yarn why, yarn info, yarn config list, yarn config get"
            + "\n    # pnpm (read-only)\n    pnpm list, pnpm ls, pnpm outdated, pnpm why, pnpm config list, pnpm config get"
            + "\n    # bun (read-only)\n    bun pm ls, bun pm hash"
            + "\n    # js install\n    npm install, yarn install, pnpm install, bun install"
            + "\n    # Modern Unix (read-only)\n    bat, eza, exa, fd, fzf, dust, duf, tldr, zoxide, httpie, http, jq, yq"
            + "\n    # Docker (read-only)\n    docker ps, docker images, docker logs, docker inspect, docker stats, docker top, docker diff, docker history, docker version, docker info"
            + "\n    # Docker (workspace - reversible)\n    docker pull, docker build"
            + "\n    # AWS (read-only)\n    aws s3 ls, aws s3 cp, aws sts get-caller-identity, aws iam get-user, aws iam list-users"
            + "\n    aws ec2 describe-instances, aws ec2 describe-security-groups\n    aws ec2 describe-vpcs"
            + "\n    aws logs describe-log-groups, aws logs filter-log-events, aws logs get-log-events"
            + "\n    aws lambda list-functions, aws lambda get-function"
            + "\n    aws cloudformation describe-stacks, aws cloudformation list-stacks"
            + "\n    aws rds describe-db-instances, aws dynamodb list-tables, aws dynamodb describe-table"
            + "\n    aws sqs list-queues, aws sns list-topics"
            + "\n    aws configure list, aws configure get"
            + "\n    # GCloud (read-only)\n    gcloud config list, gcloud config get-value, gcloud auth list"
            + "\n    gcloud projects list, gcloud projects describe"
            + "\n    gcloud compute instances list, gcloud compute instances describe, gcloud compute zones list, gcloud compute regions list"
            + "\n    gcloud container clusters list, gcloud container clusters describe"
            + "\n    gcloud functions list, gcloud functions describe, gcloud functions logs read"
            + "\n    gcloud run services list, gcloud run services describe"
            + "\n    gcloud sql instances list, gcloud sql instances describe"
            + "\n    gcloud storage ls, gcloud storage cat"
            + "\n    gcloud logging read"
            + "\n    # toolslm\n            folder2ctx, repo2ctx`n            # Self-management`n            shellshelter"
            + "\n    # Positional exec/dest handling\n    env:exec=$0, xargs:exec=$0"
            + "\n    tee:dest=$0, ex:dest=$2, cp:dest=$-1, mv:dest=$-1, mkdir:dest=$-1"
            + "\n    # Exec/dest flag handling\n    find:-delete|-ok|-okdir:exec=-exec|-execdir"
            + "\n    rg:--pre"
            + "\n    tar:--use-compress-program|--transform|--checkpoint-action|--info-script|--new-volume-script:exec=--to-command|-I"
            + "\n    curl:dest=-o|--output"
            + "\n    # Builtins\n    cd, pwd, export, test, [, true, false";

    // ---------------------------------------------------------------------------
    // PowerShell defaults (defined before BashDefaultJson so it can reference them)
    // ---------------------------------------------------------------------------

    private static readonly string[] PsDefaultOkDests =
    [
        ".\\",
        "$env:TEMP",
    ];

    private static readonly string[] PsDefaultOkCmds =
    [
        // Navigation
        "Get-ChildItem", "Get-Location", "Set-Location", "Push-Location", "Pop-Location",
        // Content
        "Get-Content", "Select-Object", "Where-Object", "ForEach-Object", "Sort-Object",
        "Group-Object", "Measure-Object", "Compare-Object", "Select-String", "Get-Unique",
        "Format-Table", "Format-List", "Format-Wide", "Format-Hex",
        // System info
        "Get-Process", "Get-Service", "Get-Date", "Get-Host", "Get-ComputerInfo",
        "Get-EventLog", "Get-WinEvent",
        // Environment
        "Get-Variable", "Get-Alias", "Get-Command", "Get-Help", "Get-Member",
        "Get-Module", "Get-PSProvider", "Get-PSDrive",
        // Path
        "Resolve-Path", "Split-Path", "Join-Path", "Test-Path", "Convert-Path",
        // Web (read-only)
        "Invoke-WebRequest", "Invoke-RestMethod",
        // Output
        "Write-Output", "Write-Host", "Write-Verbose", "Write-Debug",
        "Out-String", "Out-Host", "Out-Null",
        // Misc utilities
        "Write-Output", "Clear-Host", "Start-Sleep",
        // Git (read-only) — same git subcommands as bash defaults
        "git blame", "git branch", "git cat-file", "git config --get", "git config --list",
        "git describe", "git diff", "git log", "git ls-files", "git ls-tree", "git merge-base",
        "git remote", "git rev-parse", "git shortlog", "git show", "git stash list", "git status", "git tag",
        // Git (workspace)
        "git fetch", "git add", "git commit", "git switch", "git checkout",
        // gh (read-only)
        "gh repo view", "gh issue list", "gh issue view", "gh pr list", "gh pr view",
        "gh pr status", "gh pr checks", "gh pr diff",
        "gh release list", "gh release view", "gh run list", "gh run view",
        "gh workflow list", "gh workflow view",
        "gh auth status", "gh gist list", "gh gist view", "gh browse", "gh search",
        // dotnet (read-only)
        "dotnet build", "dotnet test", "dotnet restore", "dotnet list package",
        "dotnet list reference", "dotnet format --verify-no-changes",
        // Self-management
        "shellshelter",
        // Tee (write dest validated)
        "Tee-Object:dest=-FilePath|-LiteralPath",
    ];

    /// <summary>
    /// Gets the built-in bash defaults encoded in the native JSON configuration format.
    /// </summary>
    public static string BashDefaultJson { get; } = JsonSerializer.Serialize(
        new
        {
            bash = new
            {
                okDests = BashDefaultOkDests,
                okCmds = BashDefaultOkCmds,
            },
            powershell = new
            {
                okDests = PsDefaultOkDests,
                okCmds = PsDefaultOkCmds,
            },
        },
        new JsonSerializerOptions
        {
            WriteIndented = true,
        });

    /// <summary>
    /// Gets the built-in PowerShell default INI configuration section.
    /// </summary>
    public const string PsDefaultIni = """
        [POWERSHELL]
        ok_dests = .\, $env:TEMP

        ok_cmds = Get-ChildItem, Get-Location, Set-Location, Push-Location, Pop-Location
            Get-Content, Select-Object, Where-Object, ForEach-Object, Sort-Object
            Group-Object, Measure-Object, Compare-Object, Select-String, Get-Unique
            Format-Table, Format-List, Format-Wide, Format-Hex
            Get-Process, Get-Service, Get-Date, Get-Host, Get-ComputerInfo
            Get-EventLog, Get-WinEvent
            Get-Variable, Get-Alias, Get-Command, Get-Help, Get-Member
            Get-Module, Get-PSProvider, Get-PSDrive
            Resolve-Path, Split-Path, Join-Path, Test-Path, Convert-Path
            Invoke-WebRequest, Invoke-RestMethod
            Write-Output, Write-Host, Write-Verbose, Write-Debug
            Out-String, Out-Host, Out-Null, Clear-Host, Start-Sleep
            git blame, git branch, git cat-file, git config --get, git config --list,
            git describe, git diff, git log, git ls-files, git ls-tree, git merge-base,
            git remote, git rev-parse, git shortlog, git show, git stash list, git status, git tag
            git fetch, git add, git commit, git switch, git checkout
            gh repo view, gh issue list, gh issue view, gh pr list, gh pr view,
            gh pr status, gh pr checks, gh pr diff,
            gh release list, gh release view, gh run list, gh run view,
            gh workflow list, gh workflow view,
            gh auth status, gh gist list, gh gist view, gh browse, gh search
            dotnet build, dotnet test, dotnet restore, dotnet list package,
            dotnet list reference, dotnet format --verify-no-changes
            shellshelter
            Tee-Object:dest=-FilePath|-LiteralPath
        """;
}
