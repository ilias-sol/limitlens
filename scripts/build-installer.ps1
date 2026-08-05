[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PublishDirectory,
    [Parameter(Mandatory)]
    [string]$OutputDirectory,
    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$script = Join-Path $root 'installer\LimitLens.iss'
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null

$compilerCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
$compilerPath = if ($compilerCommand) { $compilerCommand.Source } else { $null }
if (-not $compilerPath) {
    $candidates = @(
        (Join-Path $root 'artifacts\tools\InnoSetup6\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    $compilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if (-not $compilerPath) {
    throw 'Inno Setup 6 was not found. Install it or run the release GitHub workflow.'
}

& $compilerPath "/DAppVersion=$Version" "/DPublishDir=$publish" "/DOutputDir=$output" $script
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}
