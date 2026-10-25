param([string]$Destination, [switch]$Launch, [switch]$Diagnostics)
$ErrorActionPreference = 'Stop'
$config = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'pet.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$source = $PSScriptRoot
if (!(Test-Path -LiteralPath (Join-Path $source $config.exe))) {
    $source = Join-Path $PSScriptRoot ('dist\' + $config.package)
}
if (!(Test-Path -LiteralPath (Join-Path $source $config.exe))) { throw '请先运行 build.ps1，或从 Releases 下载并完整解压程序包。' }
if (!$Destination) { $Destination = Join-Path $env:LOCALAPPDATA ('Programs\' + $config.package) }
$Destination = [IO.Path]::GetFullPath($Destination)
$source = [IO.Path]::GetFullPath($source)
if ($Destination -eq $source) { throw '安装目录必须与程序包目录不同。' }
$prefix = $source.TrimEnd('\') + '\'
if ($Destination.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw '安装目录不能位于程序包内部。' }
$processName = [IO.Path]::GetFileNameWithoutExtension($config.exe)
if (Get-Process -Name $processName -ErrorAction SilentlyContinue) { throw '请先从右键菜单退出这个角色，再安装或更新。' }
$markerPath = Join-Path $Destination '.meme-pet-install.json'
if (Test-Path -LiteralPath $Destination) {
    $directory = Get-Item -LiteralPath $Destination
    if (!$directory.PSIsContainer -or ($directory.Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw '安装目标必须是普通文件夹。' }
    if (@(Get-ChildItem -LiteralPath $Destination -Force).Count -gt 0) {
        if (!(Test-Path -LiteralPath $markerPath)) { throw '目标文件夹已有其他文件。请选择空文件夹，避免覆盖现有内容。' }
        $owner = Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ($owner.package -ne $config.package -or $owner.role -ne $config.role) { throw '目标文件夹属于其他桌宠，不能覆盖。' }
    }
}
$shell = New-Object -ComObject WScript.Shell
$shortcutPaths = @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs')) | ForEach-Object { Join-Path $_ ($config.shortcut + '.lnk') }
foreach ($shortcutPath in $shortcutPaths) {
    if (Test-Path -LiteralPath $shortcutPath) {
        $existing = $shell.CreateShortcut($shortcutPath)
        if ($existing.TargetPath -ne (Join-Path $Destination $config.exe)) { throw ('已有同名快捷方式指向其他程序，请先自行处理：' + $shortcutPath) }
    }
}
New-Item -ItemType Directory -Path $Destination -Force | Out-Null
@{ package=$config.package; role=$config.role } | ConvertTo-Json | Set-Content -LiteralPath $markerPath -Encoding UTF8
foreach ($item in Get-ChildItem -LiteralPath $source -Force) { Copy-Item -LiteralPath $item.FullName -Destination $Destination -Recurse -Force }
foreach ($shortcutPath in $shortcutPaths) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $Destination $config.exe
    $shortcut.WorkingDirectory = $Destination
    $shortcut.Description = $config.title
    $shortcut.Save()
}
Write-Output ('Installed: ' + (Join-Path $Destination $config.exe))
if ($Launch) {
    $start = @{ FilePath = (Join-Path $Destination $config.exe); WorkingDirectory = $Destination; WindowStyle = 'Normal' }
    if ($Diagnostics) { $start.ArgumentList = '--diagnostics' }
    Start-Process @start
}
