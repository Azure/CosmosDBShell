#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Runs the unit tests with code coverage and generates a human readable report.

.DESCRIPTION
    Collects coverage with coverlet (via "dotnet test --collect") and renders the
    result with ReportGenerator. The report contains an overall summary as well as
    a per-namespace and per-class breakdown.

    An interactive HTML report is written to the output directory and a text summary
    is printed to the console. The HTML report has a "Group by" selector that allows
    grouping the coverage numbers by namespace.

.PARAMETER Configuration
    Build configuration to test. Defaults to "Debug".

.PARAMETER Output
    Directory for the generated coverage report. Defaults to "TestResults/coverage".

.PARAMETER NoOpen
    Do not open the generated HTML report in the default browser.

.EXAMPLE
    ./tools/coverage.ps1

.EXAMPLE
    ./tools/coverage.ps1 -Configuration Release -NoOpen
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [string]$Output = 'TestResults/coverage',
    [switch]$NoOpen
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    $testProject = Join-Path $repoRoot 'CosmosDBShell.Tests/CosmosDBShell.Tests.csproj'
    $resultsDir = Join-Path $repoRoot 'TestResults/coverage-raw'
    $reportDir = Join-Path $repoRoot $Output

    if (Test-Path $resultsDir) {
        Remove-Item -Recurse -Force $resultsDir
    }

    Write-Host 'Restoring local dotnet tools...' -ForegroundColor Cyan
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed with exit code $LASTEXITCODE." }

    Write-Host 'Running tests with coverage...' -ForegroundColor Cyan
    dotnet test $testProject `
        --configuration $Configuration `
        --collect:'XPlat Code Coverage' `
        --results-directory $resultsDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed with exit code $LASTEXITCODE." }

    $coverageFiles = Get-ChildItem -Path $resultsDir -Recurse -Filter 'coverage.cobertura.xml'
    if (-not $coverageFiles) {
        throw "No coverage files found under '$resultsDir'."
    }

    Write-Host 'Generating coverage report...' -ForegroundColor Cyan
    $reports = ($coverageFiles.FullName -join ';')
    dotnet tool run reportgenerator `
        "-reports:$reports" `
        "-targetdir:$reportDir" `
        '-reporttypes:Html;TextSummary' `
        '-title:CosmosDBShell Code Coverage'
    if ($LASTEXITCODE -ne 0) { throw "reportgenerator failed with exit code $LASTEXITCODE." }

    $summaryFile = Join-Path $reportDir 'Summary.txt'
    if (Test-Path $summaryFile) {
        Write-Host ''
        Get-Content $summaryFile | Write-Host
    }

    $htmlReport = Join-Path $reportDir 'index.html'
    Write-Host ''
    Write-Host "HTML report: $htmlReport" -ForegroundColor Green
    Write-Host 'Tip: use the "Group by" selector in the report to view coverage per namespace.' -ForegroundColor DarkGray

    if (-not $NoOpen -and (Test-Path $htmlReport)) {
        if ($IsWindows -or $null -eq $IsWindows) {
            Start-Process $htmlReport
        }
    }
}
finally {
    Pop-Location
}
