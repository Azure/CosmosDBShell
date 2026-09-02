[CmdletBinding()]
param(
    [switch] $VerifyOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $PSScriptRoot 'CosmosDBShell.Localization.csproj'
$source = Join-Path $repoRoot 'CosmosDBShell\lang\en.ftl'
$catalog = Join-Path $repoRoot 'l10n\CosmosDBShell.json'

dotnet run --project $project --no-restore -- verify $source $catalog
if ($LASTEXITCODE -ne 0) {
    throw 'The canonical localization catalog is not synchronized with en.ftl.'
}

if ($VerifyOnly) {
    return
}

Get-ChildItem (Join-Path $repoRoot 'l10n') -Filter 'CosmosDBShell.*.json' -File | ForEach-Object {
    if ($_.Name -notmatch '^CosmosDBShell\.(?<locale>[^.]+(?:-[^.]+)?)\.json$') {
        return
    }

    $locale = $Matches.locale
    $output = Join-Path $repoRoot "CosmosDBShell\lang\$locale.ftl"
    dotnet run --project $project --no-restore -- import $source $_.FullName $output
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to generate the Fluent resource for locale '$locale'."
    }
}
