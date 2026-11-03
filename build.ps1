$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'Windows .NET Framework 4.x compiler is required.' }
$outputDir = Join-Path $projectDir 'dist'
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
$sourceFiles = @('Native.cs','Core.cs','Transport.cs','Ui.cs','Program.cs','SelfTest.cs') | ForEach-Object { Join-Path $projectDir $_ }
$references = @('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll')
& $compilerPath /nologo /utf8output /codepage:65001 /platform:x64 /optimize+ /target:winexe "/out:$outputDir\AgentMouse.exe" @references @sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Controller build failed.' }
& $compilerPath /nologo /utf8output /codepage:65001 /platform:x64 /optimize+ /target:exe /define:CLI "/out:$outputDir\AgentMouse.Cli.exe" @references @sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'CLI build failed.' }
$wpfDir = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
$compatReferences = @("/r:$wpfDir\UIAutomationClient.dll", "/r:$wpfDir\UIAutomationTypes.dll", "/r:$wpfDir\WindowsBase.dll", '/r:Microsoft.CSharp.dll')
$compatSources = @('Native.cs','Core.cs','Transport.cs','Ui.cs','CompatibilityTest.cs') | ForEach-Object { Join-Path $projectDir $_ }
& $compilerPath /nologo /utf8output /codepage:65001 /platform:x64 /optimize+ /target:exe "/out:$outputDir\AgentMouse.Compat.exe" @references @compatReferences @compatSources
if ($LASTEXITCODE -ne 0) { throw 'Compatibility harness build failed.' }
Write-Output "Built controller, CLI and compatibility harness: $outputDir"
