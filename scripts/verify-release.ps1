[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ReleaseDirectory,
    [Parameter(Mandatory)]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$expected = @(
    "LimitLens-Setup-$Version-win-x64.exe",
    "LimitLens-$Version-portable-win-x64.zip",
    "LimitLens-$Version-showcase-win-x64.zip"
)

foreach ($name in $expected) {
    if (-not (Test-Path -LiteralPath (Join-Path $release $name) -PathType Leaf)) {
        throw "Missing release artifact: $name"
    }
}

$checksumPath = Join-Path $release 'SHA256SUMS.txt'
$checksumLines = @(Get-Content -LiteralPath $checksumPath)
if ($checksumLines.Count -ne $expected.Count) {
    throw "Expected $($expected.Count) checksum entries, found $($checksumLines.Count)."
}

foreach ($line in $checksumLines) {
    if ($line -notmatch '^([a-f0-9]{64}) \*(.+)$') {
        throw 'SHA256SUMS.txt contains an invalid line.'
    }

    $path = Join-Path $release $Matches[2]
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Checksum references a missing file: $($Matches[2])"
    }

    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Matches[1]) {
        throw "Checksum mismatch: $($Matches[2])"
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($variant in @('portable', 'showcase')) {
    $zipPath = Join-Path $release "LimitLens-$Version-$variant-win-x64.zip"
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $entries = @($archive.Entries | ForEach-Object FullName)
        if ($entries -notcontains 'LimitLens.exe' -or $entries -notcontains 'portable.flag') {
            throw "$variant ZIP is missing LimitLens.exe or portable.flag."
        }
        if ($variant -eq 'showcase' -and $entries -notcontains 'showcase.flag') {
            throw 'Showcase ZIP is missing showcase.flag.'
        }
        if ($variant -eq 'portable' -and $entries -contains 'showcase.flag') {
            throw 'Portable ZIP unexpectedly contains showcase.flag.'
        }
        if ($entries | Where-Object { $_ -match '^(?:Data[/\\]|.*\.(?:pdb|user)$)' }) {
            throw "$variant ZIP contains private data or development files."
        }
        if ($entries | Where-Object { $_ -match '(?i)codex.?usage' }) {
            throw "$variant ZIP contains a legacy product name."
        }
    }
    finally {
        $archive.Dispose()
    }
}

$installerInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo(
    (Join-Path $release "LimitLens-Setup-$Version-win-x64.exe"))
if ($installerInfo.ProductName.Trim() -ne 'Limit Lens') {
    throw "Unexpected installer product name: $($installerInfo.ProductName)"
}

Write-Output "Release verification passed for Limit Lens $Version."
