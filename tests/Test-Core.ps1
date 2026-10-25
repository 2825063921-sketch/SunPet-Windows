$ErrorActionPreference = 'Stop'
$project = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$testOutput = Join-Path $project 'build\tests'
New-Item -ItemType Directory -Force -Path $testOutput | Out-Null
$output = Join-Path $testOutput 'CoreTests.exe'
& $compiler /nologo /warnaserror+ /out:$output (Join-Path $project 'src\Core.cs') (Join-Path $PSScriptRoot 'CoreTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Core test compilation failed.' }
& $output
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
