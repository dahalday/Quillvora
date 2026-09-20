$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    dotnet publish Transcriber/Transcriber.csproj -c Release -r win-x64 --self-contained true -o dist/Quillvora --packages .packages
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    if (Test-Path -LiteralPath 'models/ggml-tiny.bin') {
        New-Item -ItemType Directory -Force 'dist/Quillvora/Models' | Out-Null
        Copy-Item -LiteralPath 'models/ggml-tiny.bin' -Destination 'dist/Quillvora/Models/ggml-tiny.bin'
    }
    Copy-Item -LiteralPath README.md,THIRD_PARTY.md,HANDOFF.md,USER_MANUAL.md,VALIDATION.md -Destination dist/Quillvora
    if (Test-Path -LiteralPath Licenses) { Copy-Item -LiteralPath Licenses -Destination dist/Quillvora -Recurse -Force }
    Write-Host 'Ready: dist/Quillvora/Quillvora.exe'
} finally { Pop-Location }
