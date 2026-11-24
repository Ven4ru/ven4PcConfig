# Сборка ven4PcConfig и пакет для флешки.
#   pwsh -File build.ps1
# Результат: dist\ven4PcConfig\ (exe, exe.config, README.txt) и dist\ven4PcConfig.zip
# Нужен любой .NET SDK 6+ (сборки .NET Framework 2.0 он берёт из NuGet сам).
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src = Join-Path $root 'src'
$out = Join-Path $src 'bin\Release\net20'
$pkg = Join-Path $root 'dist\ven4PcConfig'
$zip = Join-Path $root 'dist\ven4PcConfig.zip'

dotnet build (Join-Path $src 'PcInventory.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась' }

# Папку пакета не очищаем: в dist\ven4PcConfig могут лежать собранные данные и реестр
New-Item -ItemType Directory -Force $pkg | Out-Null
Copy-Item (Join-Path $out 'ven4PcConfig.exe'), (Join-Path $out 'ven4PcConfig.exe.config') $pkg -Force
Copy-Item (Join-Path $root 'package\README.txt') $pkg -Force

# В архив — только программа и инструкция, без данных и реестра
$stage = Join-Path ([IO.Path]::GetTempPath()) ('ven4PcConfig-pack-' + [Guid]::NewGuid().ToString('N'))
$stagePkg = Join-Path $stage 'ven4PcConfig'
New-Item -ItemType Directory -Force $stagePkg | Out-Null
Copy-Item (Join-Path $pkg 'ven4PcConfig.exe'), (Join-Path $pkg 'ven4PcConfig.exe.config'), (Join-Path $pkg 'README.txt') $stagePkg
Compress-Archive -Path $stagePkg -DestinationPath $zip -Force
Remove-Item -Recurse -Force $stage

$ver = (Get-Item (Join-Path $pkg 'ven4PcConfig.exe')).VersionInfo.ProductVersion
Write-Host "Готово: ven4PcConfig $ver -> $zip"
