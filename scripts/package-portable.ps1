[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,
    [Parameter(Mandatory)]
    [string]$OutputDirectory,
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$')]
    [string]$Version,
    [switch]$Showcase
)

$ErrorActionPreference = 'Stop'
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
$workspace = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$workspacePrefix = $workspace.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($output -ne $workspace -and
    -not $output.StartsWith($workspacePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Portable output must stay inside the workspace: $workspace"
}

New-Item -ItemType Directory -Path $output -Force | Out-Null
$variant = if ($Showcase) { 'showcase' } else { 'portable' }
$stage = Join-Path $output ".LimitLens-$Version-$variant-stage-$PID-$([Guid]::NewGuid().ToString('N'))"
$zip = Join-Path $output "LimitLens-$Version-$variant-win-x64.zip"
try {
    New-Item -ItemType Directory -Path $stage | Out-Null
    Copy-Item -Path (Join-Path $publish '*') -Destination $stage -Recurse -Force
    New-Item -ItemType File -Path (Join-Path $stage 'portable.flag') -Force | Out-Null
    if ($Showcase) {
        New-Item -ItemType File -Path (Join-Path $stage 'showcase.flag') -Force | Out-Null
    }
    if (Test-Path -LiteralPath $zip) {
        Remove-Item -LiteralPath $zip -Force
    }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
}
finally {
    if (Test-Path -LiteralPath $stage) {
        $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
        $outputPrefix = $output.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        if (-not $resolvedStage.StartsWith($outputPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clear an unexpected portable staging path: $resolvedStage"
        }
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
Write-Output $zip
