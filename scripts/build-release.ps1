$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
Push-Location $projectDir
try {
    node scripts/build-native.mjs
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
    npm.cmd test
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    & (Join-Path $projectDir 'dist/AgentMouse.Cli.exe') --state-self-test
    if ($LASTEXITCODE -ne 0) { throw 'Native state checks failed.' }
    $releaseDir = Join-Path $projectDir 'release'
    New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
    $stagingDir = Join-Path $releaseDir ([guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stagingDir | Out-Null
    npm.cmd pack --ignore-scripts --pack-destination $stagingDir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Packaging failed.' }
    $archive = Get-ChildItem -LiteralPath $stagingDir -Filter '*.tgz' | Select-Object -First 1
    tar.exe -xzf $archive.FullName -C $stagingDir
    if ($LASTEXITCODE -ne 0) { throw 'Archive extraction failed.' }
    $version = (Get-Content -LiteralPath (Join-Path $projectDir 'package.json') -Raw | ConvertFrom-Json).version
    $zipPath = Join-Path $releaseDir "AgentMouse-$version-windows-x64.zip"
    if (Test-Path -LiteralPath $zipPath) { throw "Release already exists: $zipPath" }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $stagingDir 'package'), $zipPath)
    $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    [System.IO.File]::WriteAllText("$zipPath.sha256", "$hash  $([System.IO.Path]::GetFileName($zipPath))`n")
    Write-Output "Built $zipPath (runtime, binaries, installer dependencies and Apache license included)."
} finally { Pop-Location }
