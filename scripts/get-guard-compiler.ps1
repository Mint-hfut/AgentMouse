$ErrorActionPreference='Stop'
$root=Join-Path ([IO.Path]::GetTempPath()) 'agentmouse-guard-toolchain-20261104'
$compiler=Join-Path $root 'llvm-mingw-20261104-ucrt-x86_64\bin\x86_64-w64-mingw32-gcc.exe'
if(-not(Test-Path -LiteralPath $compiler)) {
    New-Item -ItemType Directory -Force $root | Out-Null
    $archive=Join-Path $root 'toolchain.zip'
    Invoke-WebRequest 'https://github.com/mstorsjo/llvm-mingw/releases/download/20261104/llvm-mingw-20261104-ucrt-x86_64.zip' -OutFile $archive
    if((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne '552191B5754C49A5A20723276A6FC56541EA1922C4C50B7B669022333FC1B40F'){throw 'Guard compiler checksum mismatch'}
    Expand-Archive -LiteralPath $archive -DestinationPath $root
}
Write-Output $compiler
