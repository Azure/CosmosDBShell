param([string]$Configuration = 'Debug')

$project = Join-Path $PSScriptRoot '..\..\CosmosDBShell\CosmosDBShell.csproj'
$assembly = dotnet msbuild $project -nologo "-p:Configuration=$Configuration" -getProperty:TargetPath
if ($LASTEXITCODE -ne 0 -or -not $assembly -or -not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
    throw "Built CosmosDBShell assembly not found for $Configuration`: $assembly"
}
$assembly
