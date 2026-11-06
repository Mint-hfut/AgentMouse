$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$testRoot = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) ('agentmouse-source-' + [guid]::NewGuid().ToString('N'))))
try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    $files = @('fusion','fusion-entry.mjs','fusion-worker.mjs','vendor','THIRD_PARTY_NOTICES.md','LICENSE','package.json','package-lock.json','plugin.json','mcp.json','.mcp.json','.claude-plugin','.codex-plugin','.zcode-plugin','install.mjs','install.cmd','mcp-server.mjs','agentmouse.mjs','browser-pointer.mjs','browser-router.mjs','bridge-client.mjs','bridge-server.mjs','browser-extension','DesktopInventory.cs','Native.cs','WindowCapture.cs','Core.cs','Transport.cs','Ui.cs','Program.cs','SelfTest.cs','scripts','skills','commands','assets','adapters')
    foreach ($file in $files) { if ($file -ne 'vendor') { Copy-Item -LiteralPath (Join-Path $projectDir $file) -Destination $testRoot -Recurse } }
    $vendorTarget = Join-Path $testRoot 'vendor\windows-mcp'
    Copy-Item -LiteralPath (Join-Path $projectDir 'ActivationGuard.cs') -Destination $testRoot
    Copy-Item -LiteralPath (Join-Path $projectDir 'licenses') -Destination $testRoot -Recurse
    New-Item -ItemType Directory -Force (Join-Path $testRoot 'dist') | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectDir 'dist\AgentMouse.Guard.current') -Destination (Join-Path $testRoot 'dist')
    Copy-Item -LiteralPath (Join-Path $projectDir 'dist\guards') -Destination (Join-Path $testRoot 'dist') -Recurse
    New-Item -ItemType Directory -Path $vendorTarget -Force | Out-Null
    foreach ($entry in @('src','pyproject.toml','uv.lock','README.md','LICENSE.md')) { Copy-Item -LiteralPath (Join-Path $projectDir "vendor\windows-mcp\$entry") -Destination $vendorTarget -Recurse }
    node (Join-Path $PSScriptRoot 'test-distribution.mjs') $testRoot
    if ($LASTEXITCODE -ne 0) { throw 'Fresh source installer checks failed.' }
} finally {
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $testRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -or -not ([System.IO.Path]::GetFileName($testRoot)).StartsWith('agentmouse-source-')) { throw 'Unsafe cleanup path.' }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
