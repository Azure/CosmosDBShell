#requires -Version 7.0
<#
.SYNOPSIS
    Computes the shared package/assembly version set used by every CosmosDBShell
    build pipeline (GitHub Actions and OneBranch / Azure DevOps).

.DESCRIPTION
    Major and Minor are read from <VersionPrefix> in Directory.Build.props so
    there is exactly one place in the repo that drives them. Patch is supplied
    by the caller (the CI run counter). The pre-release suffix and commit
    metadata are also applied here so every pipeline emits the same shape:

        Version               = <Major>.<Minor>.<Patch>
        PackageVersion        = <Major>.<Minor>.<Patch>-<Suffix>
        FileVersion           = <Major>.<Minor>.<Patch>.0
        InformationalVersion  = <Major>.<Minor>.<Patch>-<Suffix>+<CommitSha>

    Results are written to whichever CI sink is detected:
      - GitHub Actions: appends key=value lines to $env:GITHUB_OUTPUT
      - Azure DevOps  : emits ##vso[task.setvariable] and updatebuildnumber
      - Otherwise     : prints the values to stdout

.PARAMETER Patch
    The patch component (the per-run counter). Required.

.PARAMETER CommitSha
    Commit SHA to append to InformationalVersion build metadata. Optional.

.PARAMETER Suffix
    Pre-release suffix (without the leading dash). Defaults to 'preview'.

.PARAMETER RepoRoot
    Optional override for the repository root. Defaults to the parent of the
    script directory (eng/..).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateRange(0, [int]::MaxValue)]
    [int]$Patch,

    [string]$CommitSha = '',

    [string]$Suffix = 'preview',

    [string]$RepoRoot = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

$propsPath = Join-Path $RepoRoot 'Directory.Build.props'
if (-not (Test-Path $propsPath)) {
    throw "Directory.Build.props not found at '$propsPath'."
}

[xml]$props = Get-Content -LiteralPath $propsPath
$versionPrefix = $props.Project.PropertyGroup.VersionPrefix
if ([string]::IsNullOrWhiteSpace($versionPrefix)) {
    throw "VersionPrefix is not set in $propsPath."
}

$prefixParts = $versionPrefix.Split('.') | ForEach-Object { [int]$_ }
if ($prefixParts.Count -lt 2) {
    throw "VersionPrefix '$versionPrefix' must contain at least Major.Minor."
}

$major = $prefixParts[0]
$minor = $prefixParts[1]

$assemblyVersion = "$major.$minor.$Patch"
$fileVersion = "$major.$minor.$Patch.0"

$packageVersion = if ([string]::IsNullOrWhiteSpace($Suffix)) {
    $assemblyVersion
}
else {
    "$assemblyVersion-$Suffix"
}

$infoVersion = if ([string]::IsNullOrWhiteSpace($CommitSha)) {
    $packageVersion
}
else {
    "$packageVersion+$CommitSha"
}

$outputs = [ordered]@{
    assembly_version      = $assemblyVersion
    package_version       = $packageVersion
    file_version          = $fileVersion
    informational_version = $infoVersion
}

# Always echo for log visibility.
foreach ($kv in $outputs.GetEnumerator()) {
    Write-Host "$($kv.Key)=$($kv.Value)"
}

# GitHub Actions sink.
if ($env:GITHUB_OUTPUT) {
    foreach ($kv in $outputs.GetEnumerator()) {
        "$($kv.Key)=$($kv.Value)" | Out-File -FilePath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    }
}

# Azure DevOps sink.
if ($env:TF_BUILD) {
    Write-Host "##vso[task.setvariable variable=CosmosDBShell_Version]$assemblyVersion"
    Write-Host "##vso[task.setvariable variable=CosmosDBShell_PackageVersion]$packageVersion"
    Write-Host "##vso[task.setvariable variable=CosmosDBShell_FileVersion]$fileVersion"
    Write-Host "##vso[task.setvariable variable=CosmosDBShell_InformationalVersion]$infoVersion"
    Write-Host "##vso[build.updatebuildnumber]$assemblyVersion"
}
