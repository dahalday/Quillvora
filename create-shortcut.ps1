$ErrorActionPreference = 'Stop'

$appDirectory = Join-Path $PSScriptRoot 'dist\Quillvora'
$appPath = Join-Path $appDirectory 'Quillvora.exe'
if (-not (Test-Path -LiteralPath $appPath)) { throw 'Build Quillvora first using build.ps1.' }
$shell = New-Object -ComObject WScript.Shell
$shortcutPath = Join-Path $PSScriptRoot 'Quillvora.lnk'
try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $appPath
    $shortcut.WorkingDirectory = $appDirectory
    $shortcut.IconLocation = "$appPath,0"
    $shortcut.Description = 'Quillvora - Live transcription and AI summaries'
    $shortcut.Save()
    [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
} finally { [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
Write-Output "Created: $shortcutPath"
