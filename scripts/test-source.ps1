$ErrorActionPreference = 'Stop'
$projectDir = Split-Path -Parent $PSScriptRoot
$testRoot = [System.IO.Path]::GetFullPath((Join-Path ([System.IO.Path]::GetTempPath()) ('agentmouse-source-' + [guid]::NewGuid().ToString('N'))))
try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    $files = @('LICENSE','package.json','package-lock.json','plugin.json','mcp.json','.mcp.json','.claude-plugin','.codex-plugin','.zcode-plugin','install.mjs','install.cmd','mcp-server.mjs','agentmouse.mjs','browser-pointer.mjs','browser-router.mjs','bridge-client.mjs','bridge-server.mjs','browser-extension','Native.cs','WindowCapture.cs','Core.cs','Transport.cs','Ui.cs','Program.cs','SelfTest.cs','scripts','skills','commands','assets','adapters')
    foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $projectDir $file) -Destination $testRoot -Recurse }
    node (Join-Path $PSScriptRoot 'test-distribution.mjs') $testRoot
    if ($LASTEXITCODE -ne 0) { throw 'Fresh source installer checks failed.' }
} finally {
    $tempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if (-not $testRoot.StartsWith($tempRoot, [System.StringComparison]::OrdinalIgnoreCase) -or -not ([System.IO.Path]::GetFileName($testRoot)).StartsWith('agentmouse-source-')) { throw 'Unsafe cleanup path.' }
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}
