param([string]$PythonRuntime, [ValidateSet('Core','Full','Both')][string]$Edition='Both')
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
    $privateArtifacts = Get-ChildItem -LiteralPath (Join-Path $stagingDir 'package') -Recurse -Directory | Where-Object { $_.Name -in @('.venv','__pycache__') -or $_.Name.EndsWith('.egg-info') }
    if ($privateArtifacts) { throw 'Refusing to package generated Python environments/caches/installation metadata.' }
    $version = (Get-Content -LiteralPath (Join-Path $projectDir 'package.json') -Raw | ConvertFrom-Json).version
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    if ($Edition -ne 'Core' -and -not $PythonRuntime) {
        $PythonRuntime = Join-Path $stagingDir 'python-runtime'
        node scripts/build-python-runtime.mjs $PythonRuntime
        if ($LASTEXITCODE -ne 0) { throw 'Portable Python build failed.' }
    }
    if ($Edition -ne 'Core' -and -not (Test-Path -LiteralPath (Join-Path $PythonRuntime 'RUNTIME.json'))) { throw 'Portable Python manifest is missing.' }
    function Is-FusionFile([string]$Name) {
        return $Name -match '^(fusion/|vendor/|python-runtime/|fusion-(entry|worker)\.mjs$|scripts/(fusion-setup|build-python-runtime)\.mjs$)'
    }
    function Add-Tree($Zip, [string]$Root, [string]$Prefix, [string]$Edition) {
        $Root = [System.IO.Path]::GetFullPath($Root).TrimEnd('\','/')
        foreach ($file in Get-ChildItem -LiteralPath $Root -Recurse -File) {
            $name = $Prefix + $file.FullName.Substring($Root.Length + 1).Replace('\','/')
            if ($name.StartsWith('dist/guards/') -and $name -ne ('dist/guards/' + ([IO.File]::ReadAllText((Join-Path $Root 'dist/AgentMouse.Guard.current'))).Trim())) { continue }
            if ($Edition -eq 'core' -and (Is-FusionFile $name)) { continue }
            if ($Prefix -eq 'python-runtime/' -and $name -match '(__pycache__/|\.pyc$)') { continue }
            if ($name -match '(__pycache__/|\.pyc$|\.egg-info/|\.venv/)') { throw "Generated artifact in release: $name" }
            $entry = $Zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
            $output = $entry.Open()
            try {
                $text = $null
                if ($name -eq 'package.json') {
                    $metadata = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
                    $metadata | Add-Member -NotePropertyName agentmouseEdition -NotePropertyValue $Edition -Force
                    if ($Edition -eq 'core') {
                        $metadata.files = @($metadata.files | Where-Object { -not (Is-FusionFile $_) })
                        foreach ($scriptName in @($metadata.scripts.PSObject.Properties.Name | Where-Object { $_ -match 'fusion' })) { $metadata.scripts.PSObject.Properties.Remove($scriptName) }
                    }
                    $text = $metadata | ConvertTo-Json -Depth 100
                } elseif ($name -in @('mcp.json','.mcp.json','adapters/codex/mcp.json')) {
                    $metadata = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
                    $entryFile = if ($Edition -eq 'full') { 'fusion-entry.mjs' } else { 'mcp-server.mjs' }
                    $metadata.mcpServers.agentmouse.args = @($metadata.mcpServers.agentmouse.args | ForEach-Object { $_ -replace '(mcp-server|fusion-entry)\.mjs$', $entryFile })
                    $text = $metadata | ConvertTo-Json -Depth 100
                } elseif ($name -in @('plugin.json','.codex-plugin/plugin.json','.zcode-plugin/plugin.json','.claude-plugin/plugin.json')) {
                    $metadata = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
                    $display = if ($Edition -eq 'full') { 'AgentMouse Full' } else { 'AgentMouse Core' }
                    if ($metadata.PSObject.Properties.Name -contains 'interface') { $metadata.interface.displayName = $display }
                    if ($metadata.PSObject.Properties.Name -contains 'extensions') { $metadata.extensions.'com.openai'.interface.displayName = $display }
                    $metadata.description = "$display - " + $metadata.description
                    $text = $metadata | ConvertTo-Json -Depth 100
                } elseif ($name -in @('README.md','README.zh.md')) {
                    $label = if ($Edition -eq 'core') { 'AgentMouse Core: independent AgentMouse engine; no Windows-MCP or Python runtime.' } else { 'AgentMouse Full: AgentMouse Core + Windows-MCP, with a bundled isolated Python runtime; no separate Python/uv installation.' }
                    $text = "$label`n`n" + [System.IO.File]::ReadAllText($file.FullName)
                }
                if ($null -ne $text) { $bytes = [System.Text.Encoding]::UTF8.GetBytes($text); $output.Write($bytes,0,$bytes.Length) }
                else { $input = [System.IO.File]::OpenRead($file.FullName); try { $input.CopyTo($output) } finally { $input.Dispose() } }
            } finally { $output.Dispose() }
        }
    }
    $editions = if ($Edition -eq 'Both') { @('core','full') } else { @($Edition.ToLowerInvariant()) }
    foreach ($editionName in $editions) {
        $edition = $editionName
        $label = if ($edition -eq 'core') { 'Core' } else { 'Full' }
        $zipPath = Join-Path $releaseDir "AgentMouse-$label-$version-windows-x64.zip"
        if (Test-Path -LiteralPath $zipPath) { throw "Release already exists: $zipPath" }
        $zip = [System.IO.Compression.ZipFile]::Open($zipPath,[System.IO.Compression.ZipArchiveMode]::Create)
        try {
            Add-Tree $zip (Join-Path $stagingDir 'package') '' $edition
            if ($edition -eq 'full') { Add-Tree $zip ([System.IO.Path]::GetFullPath($PythonRuntime)) 'python-runtime/' $edition }
        } finally { $zip.Dispose() }
        $hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
        [System.IO.File]::WriteAllText("$zipPath.sha256", "$hash  $([System.IO.Path]::GetFileName($zipPath))`n")
        Write-Output "Built $zipPath"
    }
} finally { Pop-Location }
