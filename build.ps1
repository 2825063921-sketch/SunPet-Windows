param([switch]$Package)
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$config = Get-Content -LiteralPath (Join-Path $project 'pet.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { throw '需要 Windows 10/11 x64 和 .NET Framework 4.8。' }
$release = Join-Path $project ('dist\' + $config.package)
$running = Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($config.exe)) -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq (Join-Path $release $config.exe) }
if ($running) { throw '请先从右键菜单退出此目录中的桌宠，再重新构建。' }
New-Item -ItemType Directory -Path $release -Force | Out-Null
$references = @('WPF\PresentationFramework.dll','WPF\PresentationCore.dll','WPF\WindowsBase.dll','System.Xaml.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Drawing.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$sources = @(Get-ChildItem -LiteralPath (Join-Path $project 'src') -Filter '*.cs' | ForEach-Object FullName)
if (!$sources) { throw 'src 目录缺少应用源码。' }
$options = @('/nologo','/codepage:65001','/warn:4','/warnaserror+','/target:winexe','/platform:x64','/optimize+',('/out:' + (Join-Path $release $config.exe)),('/win32manifest:' + (Join-Path $project 'Pet.manifest')))
& $compiler @options @references @sources
if ($LASTEXITCODE -ne 0) { throw '桌宠编译失败。' }
Copy-Item -LiteralPath (Join-Path $project 'Assets') -Destination $release -Recurse -Force
Copy-Item -LiteralPath (Join-Path $project 'licenses') -Destination $release -Recurse -Force
foreach ($name in @('pet.json','LICENSE','NOTICE.md','README.txt','install.ps1')) { Copy-Item -LiteralPath (Join-Path $project $name) -Destination $release -Force }
Write-Output ('Built: ' + (Join-Path $release $config.exe))
if ($Package) {
    $output = Join-Path $project 'outputs'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $archive = Join-Path $output ($config.package + '-' + $config.version + '.zip')
    Compress-Archive -LiteralPath $release -DestinationPath $archive -Force
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $output 'SHA256SUMS.txt'), ($hash + '  ' + [IO.Path]::GetFileName($archive) + "`n"), [Text.Encoding]::ASCII)
    Write-Output ('Package: ' + $archive)
}
