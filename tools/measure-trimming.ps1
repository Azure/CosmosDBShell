#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Publishes and compares the normal and experimental trimmed applications.

.DESCRIPTION
    Creates self-contained single-file baseline and partial-trimming publishes,
    runs process-level smoke tests, compresses both outputs, and writes Markdown
    and JSON summaries. The experiment does not change normal build or package
    settings.

.PARAMETER RuntimeIdentifier
    Runtime identifier to publish. Defaults to win-x64.

.PARAMETER Configuration
    Build configuration. Defaults to Release.

.PARAMETER Output
    Output directory relative to the repository root. Defaults to
    artifacts/trimming.

.PARAMETER Iterations
    Number of --version startup measurements per variant. Defaults to 3.

.EXAMPLE
    ./tools/measure-trimming.ps1

.EXAMPLE
    ./tools/measure-trimming.ps1 -RuntimeIdentifier linux-x64 -Iterations 5
#>
[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$RuntimeIdentifier = 'win-x64',
    [string]$Configuration = 'Release',
    [ValidateScript({
            -not [string]::IsNullOrWhiteSpace($_) -and
            -not [IO.Path]::IsPathRooted($_) -and
            '..' -notin ($_ -split '[\\/]')
        })]
    [string]$Output = 'artifacts/trimming',
    [ValidateRange(1, 20)]
    [int]$Iterations = 3
)

$ErrorActionPreference = 'Stop'

function Invoke-DotNetPublish {
    param(
        [Parameter(Mandatory)]
        [string]$Project,
        [Parameter(Mandatory)]
        [string]$PublishDirectory,
        [Parameter(Mandatory)]
        [bool]$Trimmed
    )

    $arguments = @(
        'publish',
        $Project,
        '--configuration', $Configuration,
        '--runtime', $RuntimeIdentifier,
        '--self-contained', 'true',
        '-p:PublishSingleFile=true',
        '--output', $PublishDirectory
    )

    if (-not $Trimmed) {
        $arguments += '-p:PublishTrimmed=false'
    }
    else {
        $arguments += '-p:PublishTrimmed=true'
        $arguments += '-p:TrimMode=partial'
    }

    & dotnet @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

function Invoke-SmokeTests {
    param(
        [Parameter(Mandatory)]
        [string]$Executable
    )

    & $Executable --version *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "'$Executable --version' failed with exit code $LASTEXITCODE."
    }

    & $Executable -c version *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "'$Executable -c version' failed with exit code $LASTEXITCODE."
    }

    & $Executable -c trimming-experiment-invalid-command *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "'$Executable' returned success for an invalid command."
    }
}

function Measure-StartupMilliseconds {
    param(
        [Parameter(Mandatory)]
        [string]$Executable
    )

    $measurements = for ($index = 0; $index -lt $Iterations; $index++) {
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        & $Executable --version *> $null
        $stopwatch.Stop()
        if ($LASTEXITCODE -ne 0) {
            throw "'$Executable --version' failed with exit code $LASTEXITCODE."
        }

        $stopwatch.Elapsed.TotalMilliseconds
    }

    return [Math]::Round(($measurements | Measure-Object -Average).Average, 1)
}

function Get-VariantResult {
    param(
        [Parameter(Mandatory)]
        [string]$Name,
        [Parameter(Mandatory)]
        [string]$PublishDirectory,
        [Parameter(Mandatory)]
        [string]$ZipPath,
        [Parameter(Mandatory)]
        [string]$Executable
    )

    $files = @(Get-ChildItem -Path $PublishDirectory -File -Recurse)
    $publishedBytes = ($files | Measure-Object -Property Length -Sum).Sum

    if (Test-Path $ZipPath) {
        Remove-Item -Path $ZipPath -Force
    }

    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $PublishDirectory,
        $ZipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    Invoke-SmokeTests -Executable $Executable

    return [pscustomobject]@{
        Variant                    = $Name
        PublishedBytes             = [long]$publishedBytes
        PublishedMiB               = [Math]::Round($publishedBytes / 1MB, 2)
        CompressedBytes            = [long](Get-Item $ZipPath).Length
        CompressedMiB              = [Math]::Round((Get-Item $ZipPath).Length / 1MB, 2)
        FileCount                  = $files.Count
        AverageStartupMilliseconds = Measure-StartupMilliseconds -Executable $Executable
        SmokeTests                 = 'passed'
    }
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot 'CosmosDBShell/CosmosDBShell.csproj'
$outputRoot = Join-Path $repoRoot $Output
$baselineDirectory = Join-Path $outputRoot "baseline-$RuntimeIdentifier"
$trimmedDirectory = Join-Path $outputRoot "trimmed-$RuntimeIdentifier"
$executableName = if ($RuntimeIdentifier.StartsWith('win-', [StringComparison]::OrdinalIgnoreCase)) {
    'CosmosDBShell.exe'
}
else {
    'CosmosDBShell'
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
foreach ($directory in @($baselineDirectory, $trimmedDirectory)) {
    if (Test-Path $directory) {
        Remove-Item -Path $directory -Recurse -Force
    }
}

Write-Host "Publishing baseline for $RuntimeIdentifier..." -ForegroundColor Cyan
Invoke-DotNetPublish -Project $project -PublishDirectory $baselineDirectory -Trimmed $false

Write-Host "Publishing partial-trimming experiment for $RuntimeIdentifier..." -ForegroundColor Cyan
Invoke-DotNetPublish -Project $project -PublishDirectory $trimmedDirectory -Trimmed $true

$baselineZip = Join-Path $outputRoot "baseline-$RuntimeIdentifier.zip"
$trimmedZip = Join-Path $outputRoot "trimmed-$RuntimeIdentifier.zip"
$results = @(
    Get-VariantResult `
        -Name 'baseline' `
        -PublishDirectory $baselineDirectory `
        -ZipPath $baselineZip `
        -Executable (Join-Path $baselineDirectory $executableName)
    Get-VariantResult `
        -Name 'partial-trimmed' `
        -PublishDirectory $trimmedDirectory `
        -ZipPath $trimmedZip `
        -Executable (Join-Path $trimmedDirectory $executableName)
)

$baseline = $results[0]
$trimmed = $results[1]
$publishedReduction = [Math]::Round((1 - ($trimmed.PublishedBytes / $baseline.PublishedBytes)) * 100, 1)
$compressedReduction = [Math]::Round((1 - ($trimmed.CompressedBytes / $baseline.CompressedBytes)) * 100, 1)

$summary = [pscustomobject]@{
    GeneratedAtUtc                 = [DateTimeOffset]::UtcNow.ToString('O')
    RuntimeIdentifier              = $RuntimeIdentifier
    Configuration                  = $Configuration
    Iterations                     = $Iterations
    PublishedSizeReductionPercent  = $publishedReduction
    CompressedSizeReductionPercent = $compressedReduction
    Results                        = $results
}

$jsonPath = Join-Path $outputRoot "Summary-$RuntimeIdentifier.json"
$summary | ConvertTo-Json -Depth 5 | Set-Content -Path $jsonPath -Encoding utf8

$markdown = @(
    '# CosmosDBShell trimming experiment',
    '',
    "Runtime identifier: ``$RuntimeIdentifier``  ",
    "Configuration: ``$Configuration``  ",
    "Generated: $($summary.GeneratedAtUtc)",
    '',
    '| Variant | Published | ZIP | Files | Average `--version` | Smoke tests |',
    '| --- | ---: | ---: | ---: | ---: | --- |'
)
foreach ($result in $results) {
    $markdown += "| $($result.Variant) | $($result.PublishedMiB) MiB | $($result.CompressedMiB) MiB | $($result.FileCount) | $($result.AverageStartupMilliseconds) ms | $($result.SmokeTests) |"
}

$markdown += @(
    '',
    "Published size reduction: **$publishedReduction%**  ",
    "Compressed size reduction: **$compressedReduction%**",
    '',
    '> This is an experimental partial-trimming result. Passing these smoke tests does not establish feature compatibility. Emulator, authentication, MCP, LSP, import/export, and command-parity testing are still required.'
)

$markdownPath = Join-Path $outputRoot "Summary-$RuntimeIdentifier.md"
$markdown | Set-Content -Path $markdownPath -Encoding utf8

Write-Host ''
$results | Format-Table Variant, PublishedMiB, CompressedMiB, FileCount, AverageStartupMilliseconds, SmokeTests -AutoSize
Write-Host "Published size reduction: $publishedReduction%" -ForegroundColor Green
Write-Host "Compressed size reduction: $compressedReduction%" -ForegroundColor Green
Write-Host "Summary: $markdownPath"