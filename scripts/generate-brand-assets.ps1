[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SourceSvg,
    [Parameter(Mandatory)]
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$source = (Resolve-Path -LiteralPath $SourceSvg).Path
$output = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null

[xml]$document = Get-Content -LiteralPath $source -Raw
$pathData = @($document.svg.path | ForEach-Object { $_.d })
if ($pathData.Count -eq 0) {
    throw 'The supplied SVG does not contain any path geometry.'
}

function New-LogoPng([int]$Size) {
    $visual = [System.Windows.Media.DrawingVisual]::new()
    $context = $visual.RenderOpen()
    try {
        $inset = [Math]::Max(1.0, $Size * 0.025)
        $radius = $Size * 0.22
        $background = [System.Windows.Media.SolidColorBrush]::new(
            [System.Windows.Media.Color]::FromRgb(24, 28, 34))
        $context.DrawRoundedRectangle(
            $background,
            $null,
            [System.Windows.Rect]::new($inset, $inset, $Size - (2 * $inset), $Size - (2 * $inset)),
            $radius,
            $radius)

        $padding = $Size * 0.17
        $scale = [Math]::Min(($Size - (2 * $padding)) / 694.0, ($Size - (2 * $padding)) / 601.0)
        $offsetX = ($Size - (694.0 * $scale)) / 2.0
        $offsetY = ($Size - (601.0 * $scale)) / 2.0
        $transform = [System.Windows.Media.TransformGroup]::new()
        $transform.Children.Add([System.Windows.Media.ScaleTransform]::new($scale, $scale))
        $transform.Children.Add([System.Windows.Media.TranslateTransform]::new($offsetX, $offsetY))
        $context.PushTransform($transform)
        foreach ($data in $pathData) {
            $geometry = [System.Windows.Media.Geometry]::Parse($data)
            $context.DrawGeometry([System.Windows.Media.Brushes]::White, $null, $geometry)
        }
        $context.Pop()
    }
    finally {
        $context.Close()
    }

    $bitmap = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $Size,
        $Size,
        96,
        96,
        [System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [System.IO.MemoryStream]::new()
    $encoder.Save($stream)
    return ,$stream.ToArray()
}

$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$images = @($sizes | ForEach-Object { ,(New-LogoPng $_) })
$iconPath = Join-Path $output 'LimitLens.ico'
$stream = [System.IO.File]::Open($iconPath, [System.IO.FileMode]::Create)
$writer = [System.IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$sizes.Count)
    $offset = 6 + (16 * $sizes.Count)
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $size = $sizes[$index]
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]$(if ($size -eq 256) { 0 } else { $size }))
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$index].Length)
        $writer.Write([uint32]$offset)
        $offset += $images[$index].Length
    }
    foreach ($image in $images) {
        $writer.Write($image)
    }
}
finally {
    $writer.Dispose()
    $stream.Dispose()
}

[System.IO.File]::WriteAllBytes((Join-Path $output 'LimitLens-256.png'), $images[-1])
Copy-Item -LiteralPath $source -Destination (Join-Path $output 'LimitLens.svg') -Force

Write-Output $iconPath
