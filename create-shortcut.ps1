$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$appDirectory = Join-Path $PSScriptRoot 'dist\Quillvora'
$appPath = Join-Path $appDirectory 'Quillvora.exe'
if (-not (Test-Path -LiteralPath $appPath)) { throw 'Build Quillvora first using build.ps1.' }
$iconPath = Join-Path $appDirectory 'Quillvora.ico'

# Store multiple PNG sizes in a Windows ICO for crisp Explorer shortcuts.
$images = @()
foreach ($size in @(16, 32, 48, 64, 128, 256)) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $background = New-Object System.Drawing.SolidBrush([System.Drawing.ColorTranslator]::FromHtml('#173447'))
    $white = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $accent = New-Object System.Drawing.Pen([System.Drawing.ColorTranslator]::FromHtml('#47D9B3'), 13)
    $stream = New-Object System.IO.MemoryStream
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform(($size / 256.0), ($size / 256.0))
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.FillEllipse($background, 4, 4, 248, 248)
        $graphics.FillEllipse($white, 101, 43, 54, 54)
        $graphics.FillRectangle($white, 101, 70, 54, 53)
        $graphics.FillEllipse($white, 101, 96, 54, 54)
        $accent.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $accent.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.DrawArc($accent, 78, 72, 100, 104, 0, 180)
        $graphics.DrawLine($accent, 128, 177, 128, 207)
        $graphics.DrawLine($accent, 101, 209, 155, 209)
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $images += ,($stream.ToArray())
    } finally {
        $stream.Dispose(); $accent.Dispose(); $white.Dispose(); $background.Dispose()
        $graphics.Dispose(); $bitmap.Dispose()
    }
}
$file = [System.IO.File]::Create($iconPath)
$writer = New-Object System.IO.BinaryWriter($file)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$images.Count)
    $offset = 6 + 16 * $images.Count
    $sizes = @(16, 32, 48, 64, 128, 256)
    for ($i = 0; $i -lt $images.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
        $offset += $images[$i].Length
    }
    foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
} finally { $writer.Dispose(); $file.Dispose() }

$shell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path $PSScriptRoot 'Quillvora.lnk'
try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $appPath
    $shortcut.WorkingDirectory = $appDirectory
    $shortcut.IconLocation = "$iconPath,0"
    $shortcut.Description = 'Quillvora - Live transcription and AI summaries'
    $shortcut.Save()
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
} finally { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
Write-Output "Created: $shortcutPath"
