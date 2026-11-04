$ErrorActionPreference = 'Stop'
$version = '157.0.8103.38'
$testDir = Join-Path ([System.IO.Path]::GetTempPath()) "agentmouse-cft-$version"
$chromePath = Join-Path $testDir 'chrome-win64/chrome.exe'
if (-not (Test-Path -LiteralPath $chromePath)) {
    New-Item -ItemType Directory -Path $testDir -Force | Out-Null
    $archivePath = Join-Path $testDir 'chrome.zip'
    Invoke-WebRequest -Uri "https://storage.googleapis.com/chrome-for-testing-public/$version/win64/chrome-win64.zip" -OutFile $archivePath
    Expand-Archive -LiteralPath $archivePath -DestinationPath $testDir -Force
}
Write-Output $chromePath
