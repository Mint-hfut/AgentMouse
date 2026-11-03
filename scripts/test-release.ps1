$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$version = (Get-Content -LiteralPath (Join-Path $projectDir 'package.json') -Raw | ConvertFrom-Json).version
$archivePath = Join-Path $projectDir "release/AgentMouse-$version-windows-x64.zip"
$expected = ((Get-Content -LiteralPath "$archivePath.sha256" -Raw) -split ' ')[0]
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expected) { throw 'Release checksum mismatch.' }
$testRoot = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) ('agentmouse-zip-' + [guid]::NewGuid().ToString('N'))))
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $testRoot)
    node (Join-Path $PSScriptRoot 'test-distribution.mjs') $testRoot
    if ($LASTEXITCODE -ne 0) { throw 'Distribution checks failed.' }
} finally {
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $testRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -or -not ([System.IO.Path]::GetFileName($testRoot)).StartsWith('agentmouse-zip-')) { throw 'Unsafe cleanup path.' }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
