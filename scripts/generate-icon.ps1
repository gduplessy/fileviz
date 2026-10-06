$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase
$fileVizRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fileVizAssets = Join-Path $fileVizRoot 'src/FileViz.App/Assets'
[xml]$fileVizSvg = Get-Content (Join-Path $fileVizAssets 'FileViz.svg') -Raw
$fileVizFrames = @()
foreach ($fileVizSize in @(16, 20, 24, 32, 40, 48, 64, 128, 256)) {
    $fileVizVisual = [Windows.Media.DrawingVisual]::new()
    $fileVizDrawing = $fileVizVisual.RenderOpen()
    try {
        $fileVizDrawing.PushTransform([Windows.Media.ScaleTransform]::new($fileVizSize / 16.0, $fileVizSize / 16.0))
        foreach ($fileVizRect in $fileVizSvg.svg.rect) {
            $fileVizBrush = [Windows.Media.BrushConverter]::new().ConvertFromInvariantString($fileVizRect.fill)
            $fileVizBounds = [Windows.Rect]::new([double]$fileVizRect.x, [double]$fileVizRect.y, [double]$fileVizRect.width, [double]$fileVizRect.height)
            $fileVizDrawing.DrawRoundedRectangle($fileVizBrush, $null, $fileVizBounds, [double]$fileVizRect.rx, [double]$fileVizRect.rx)
        }
        $fileVizDrawing.Pop()
    } finally { $fileVizDrawing.Close() }
    $fileVizBitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($fileVizSize, $fileVizSize, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $fileVizBitmap.Render($fileVizVisual)
    $fileVizEncoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $fileVizEncoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($fileVizBitmap))
    $fileVizPng = [IO.MemoryStream]::new()
    try {
        $fileVizEncoder.Save($fileVizPng)
        $fileVizFrames += [pscustomobject]@{ Size = $fileVizSize; Bytes = $fileVizPng.ToArray() }
    } finally { $fileVizPng.Dispose() }
}
$fileVizOutput = [IO.File]::Create((Join-Path $fileVizAssets 'FileViz.ico'))
$fileVizWriter = [IO.BinaryWriter]::new($fileVizOutput)
try {
    $fileVizWriter.Write([uint16]0)
    $fileVizWriter.Write([uint16]1)
    $fileVizWriter.Write([uint16]$fileVizFrames.Count)
    $fileVizOffset = 6 + 16 * $fileVizFrames.Count
    foreach ($fileVizFrame in $fileVizFrames) {
        $fileVizDimension = if ($fileVizFrame.Size -eq 256) { 0 } else { $fileVizFrame.Size }
        $fileVizWriter.Write([byte]$fileVizDimension)
        $fileVizWriter.Write([byte]$fileVizDimension)
        $fileVizWriter.Write([uint16]0)
        $fileVizWriter.Write([uint16]1)
        $fileVizWriter.Write([uint16]32)
        $fileVizWriter.Write([uint32]$fileVizFrame.Bytes.Length)
        $fileVizWriter.Write([uint32]$fileVizOffset)
        $fileVizOffset += $fileVizFrame.Bytes.Length
    }
    foreach ($fileVizFrame in $fileVizFrames) { $fileVizWriter.Write([byte[]]$fileVizFrame.Bytes) }
} finally { $fileVizWriter.Dispose() }
Write-Output 'Generated FileViz.ico: nine transparent frames, 16–256 pixels.'
