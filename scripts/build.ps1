$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src\MonsterSanctuaryMod\MonsterSanctuaryMod.csproj'
$output = Join-Path $root 'src\MonsterSanctuaryMod\bin\Release\netstandard2.0'
$plugins = Join-Path $root 'BepInEx\plugins'

dotnet build $project --configuration Release
New-Item -ItemType Directory -Path $plugins -Force | Out-Null
Copy-Item (Join-Path $output 'MonsterSanctuaryMod.dll') $plugins -Force

Write-Host "Installed MonsterSanctuaryMod.dll into $plugins"
