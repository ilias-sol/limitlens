[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,
    [Parameter(Mandatory)]
    [string]$OutputDirectory,
    [Parameter(Mandatory)]
    [string]$Version,
    [switch]$Showcase
)

$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$workspace = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if (-not $output.StartsWith($workspace, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Portable output must stay inside the workspace: $workspace"
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
$variant = if ($Showcase) { 'showcase' } else { 'portable' }
$stage = Join-Path $output "LimitLens-$Version-$variant-win-x64"
if (Test-Path -LiteralPath $stage) {
    $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
    if (-not $resolvedStage.StartsWith($output, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace an unexpected portable staging path."
    }
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}

New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item -Path (Join-Path $publish '*') -Destination $stage -Recurse -Force
New-Item -ItemType File -Path (Join-Path $stage 'portable.flag') -Force | Out-Null
if ($Showcase) {
    New-Item -ItemType File -Path (Join-Path $stage 'showcase.flag') -Force | Out-Null
}
$zip = Join-Path $output "LimitLens-$Version-$variant-win-x64.zip"
if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Write-Output $stage
Write-Output $zip
