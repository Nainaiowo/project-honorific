param(
    [string] $Project = "",

    [string] $Activity = "Developing",

    [string] $Details = "",

    [string] $Title = "",

    [string] $Category = "development",

    [string] $Workspace = (Get-Location).Path,

    [string] $ProjectRoot = "$env:USERPROFILE\Projects",

    [string] $Path = "$env:USERPROFILE\.codex\project-honorific-status.json"
)

$ErrorActionPreference = "Stop"

function Get-GitRoot {
    param([string] $Path)

    $gitRoot = & git -C $Path rev-parse --show-toplevel 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitRoot)) {
        return [string] $gitRoot
    }

    return [System.IO.Path]::GetFullPath($Path)
}

function Convert-RepositoryNameToProjectName {
    param([string] $RepositoryName)

    $knownProjects = @{
        "better-deaths" = "Better Deaths"
        "dmu-p4-debuff-helper" = "DMU Helper"
        "chibi-chaos" = "Chibi Chaos"
        "project-honorific" = "Plugin Work"
        "project-honorific-updater" = "Plugin Work"
    }

    if ($knownProjects.ContainsKey($RepositoryName)) {
        return $knownProjects[$RepositoryName]
    }

    $words = $RepositoryName -split "[-_\s]+" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    if ($words.Count -eq 0) {
        return "Unknown Project"
    }

    return ($words | ForEach-Object {
        if ($_.Length -le 3 -and $_ -cmatch "^[A-Z0-9]+$") {
            $_
        }
        else {
            [System.Globalization.CultureInfo]::InvariantCulture.TextInfo.ToTitleCase($_.ToLowerInvariant())
        }
    }) -join " "
}

function Normalize-Activity {
    param([string] $Activity)

    switch -Regex ($Activity.Trim()) {
        "^(dev|develop|developing|work|working|coding|implement|implementing)$" { return "Developing" }
        "^(audit|auditing|review|reviewing|check|checking|verify|verifying)$" { return "Auditing" }
        "^(build|building|compile|compiling)$" { return "Building" }
        "^(test|testing|validate|validating|validation)$" { return "Testing" }
        "^(commit|committing)$" { return "Committing" }
        "^(push|pushing|push update|pushing update|release|releasing|publish|publishing|deploy|deploying)$" { return "Pushing" }
        "^(changelog|changelog update|changelog updates|update changelog|updating changelog|wording|word fixing|description|readme|notes|release notes)$" { return "Updating Changelog" }
        default {
            if ([string]::IsNullOrWhiteSpace($Activity)) {
                return "Developing"
            }

            return $Activity.Trim()
        }
    }
}

function Get-DefaultDetails {
    param(
        [string] $RawActivity,
        [string] $Details
    )

    if (-not [string]::IsNullOrWhiteSpace($Details)) {
        return $Details
    }

    switch -Regex ($RawActivity.Trim()) {
        "^(changelog|changelog update|changelog updates|update changelog|updating changelog|wording|word fixing|description|readme|notes|release notes)$" { return "Changelog updates" }
        default { return "" }
    }
}

function Format-ProjectTitle {
    param(
        [string] $Activity,
        [string] $Project
    )

    $trimmedActivity = $Activity.Trim()
    $trimmedProject = $Project.Trim()
    if ([string]::IsNullOrWhiteSpace($trimmedProject)) {
        return Get-ActivityTitleLabel -Activity $trimmedActivity
    }

    $activityLabel = Get-ActivityTitleLabel -Activity $trimmedActivity
    return "${activityLabel}: $trimmedProject"
}

function Get-ActivityTitleLabel {
    param([string] $Activity)

    switch -Regex ($Activity.Trim()) {
        "^(?i:developing)$" { return "Dev" }
        "^(?i:auditing)$" { return "Audit" }
        "^(?i:building)$" { return "Build" }
        "^(?i:testing)$" { return "Test" }
        "^(?i:committing)$" { return "Commit" }
        "^(?i:pushing)$" { return "Push" }
        "^(?i:updating changelog)$" { return "Log" }
        default { return $Activity.Trim() }
    }
}

function Test-IsUnderRoot {
    param(
        [string] $Path,
        [string] $Root
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $fullRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    return $fullPath.Equals($fullRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        $fullPath.StartsWith($fullRoot + [System.IO.Path]::AltDirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

$fullPath = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($Path))
$directory = [System.IO.Path]::GetDirectoryName($fullPath)
if (-not [string]::IsNullOrWhiteSpace($directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$workspaceRoot = Get-GitRoot -Path $Workspace
$trustedRoot = [System.IO.Path]::GetFullPath([Environment]::ExpandEnvironmentVariables($ProjectRoot))
$workspaceName = [System.IO.Path]::GetFileName($workspaceRoot)
if (-not (Test-IsUnderRoot -Path $workspaceRoot -Root $trustedRoot)) {
    throw "Workspace '$workspaceName' is outside the trusted project root. Pass -Project explicitly or set -ProjectRoot to the folder that contains your project folders."
}

if ([string]::IsNullOrWhiteSpace($Project)) {
    $Project = Convert-RepositoryNameToProjectName -RepositoryName $workspaceName
}

$rawActivity = $Activity
$Activity = Normalize-Activity -Activity $Activity
$Details = Get-DefaultDetails -RawActivity $rawActivity -Details $Details

if ([string]::IsNullOrWhiteSpace($Title)) {
    $Title = Format-ProjectTitle -Activity $Activity -Project $Project
}

$status = [ordered]@{
    version = 1
    updatedAt = [DateTimeOffset]::UtcNow.ToString("O")
    project = $Project
    activity = $Activity
    title = $Title
    details = $Details
    category = $Category
    toolProvider = "codex"
    detectable = $true
    workspace = $workspaceName
}

$json = $status | ConvertTo-Json -Depth 8
[System.IO.File]::WriteAllText($fullPath, $json + [Environment]::NewLine, [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $fullPath"
Write-Host "Project: $Project"
Write-Host "Activity: $Activity"
Write-Host "Title: $Title"
Write-Host "Workspace: $workspaceName"
