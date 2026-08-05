[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ReleaseDirectory
)

$ErrorActionPreference = 'Stop'
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$files = Get-ChildItem -LiteralPath $release -File |
    Where-Object { $_.Extension -in '.exe', '.zip' } |
    Sort-Object Name
$lines = foreach ($file in $files) {
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash *$($file.Name)"
}
$checksums = Join-Path $release 'SHA256SUMS.txt'
[System.IO.File]::WriteAllLines($checksums, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Output $checksums
