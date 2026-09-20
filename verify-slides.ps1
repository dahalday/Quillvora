$ErrorActionPreference = 'Stop'
$existingPowerPoint = @(Get-Process POWERPNT -ErrorAction SilentlyContinue).Count -gt 0
$powerPoint = New-Object -ComObject PowerPoint.Application
$presentation = $null
try {
    $slidePath = Join-Path $PSScriptRoot 'TestResults\summary.pptx'
    $previewPath = Join-Path $PSScriptRoot 'TestResults\slide-preview'
    $presentation = $powerPoint.Presentations.Open($slidePath, -1, 0, 0)
    $presentation.Export($previewPath, 'PNG', 1600, 900)
    Write-Output "PowerPoint opened and rendered $($presentation.Slides.Count) slides."
} finally {
    if ($null -ne $presentation) { $presentation.Close() }
    if (-not $existingPowerPoint) { $powerPoint.Quit() }
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($powerPoint)
}
