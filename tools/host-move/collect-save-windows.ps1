# OVER 99 Fortress Island - move host (part 1: collect, sender / old host, Windows)
# Embedded in collect-save-windows.bat (base64, decoded with certutil at run time) so the sender only needs ONE file.
# It copies this PC's world saves and the mod's vault into one ZIP on the Desktop. It never deletes or edits anything.
$ErrorActionPreference = 'Stop'

function Resolve-Game([string]$p) {
    if ([string]::IsNullOrWhiteSpace($p)) { return $null }
    $p = $p.Trim().Trim('"').TrimEnd('\')
    if (Test-Path (Join-Path $p 'How to Fish.exe')) { return $p }
    $child = Join-Path $p 'How to Fish'
    if (Test-Path (Join-Path $child 'How to Fish.exe')) { return $child }
    return $null
}

$candidates = New-Object System.Collections.Generic.List[string]
$sp = $null
try { $sp = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath } catch { }
if ($sp) {
    $candidates.Add((Join-Path $sp 'steamapps\common\How to Fish'))
    $lf = Join-Path $sp 'steamapps\libraryfolders.vdf'
    if (Test-Path $lf) {
        foreach ($m in [regex]::Matches((Get-Content $lf -Raw), '"path"\s+"([^"]+)"')) {
            $lib = $m.Groups[1].Value.Replace('\\', '\')
            $candidates.Add((Join-Path $lib 'steamapps\common\How to Fish'))
        }
    }
}
foreach ($d in [System.IO.DriveInfo]::GetDrives()) {
    try {
        if ($d.DriveType -notin @('Fixed', 'Removable')) { continue }
        $root = $d.Root
        if ([string]::IsNullOrEmpty($root)) { continue }   # some virtual drives report no root
        foreach ($rel in @('SteamLibrary', 'Steam', 'Games\SteamLibrary', 'SteamLibrary\SteamApps')) {
            $candidates.Add((Join-Path $root (Join-Path $rel 'steamapps\common\How to Fish')))
        }
    } catch { }
}

$game = $null
foreach ($c in $candidates) {
    $g = Resolve-Game $c
    if ($g) { $game = $g; break }
}
if (-not $game) {
    Write-Host ''
    Write-Host 'The game folder was not found automatically.' -ForegroundColor Yellow
    Write-Host 'Find it with: Steam -> right click How to Fish -> Manage -> Browse local files' -ForegroundColor Yellow
    $typed = Read-Host 'Paste that folder here (the one with How to Fish.exe), or press Enter to quit'
    if ($typed) { $game = Resolve-Game $typed }
}
if (-not $game) { Write-Host 'FAIL: game folder not found.' -ForegroundColor Red; exit 1 }
Write-Host ('Game folder: ' + $game)

if (Get-Process -Name 'How to Fish' -ErrorAction SilentlyContinue) {
    Write-Host 'FAIL: the game is running - close it first.' -ForegroundColor Red
    exit 1
}

$saves = Join-Path $env:USERPROFILE 'AppData\LocalLow\Dazed Games\How to Fish\Saves'
if (-not (Test-Path $saves)) { Write-Host 'FAIL: save folder not found.' -ForegroundColor Red; exit 1 }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$desk = [Environment]::GetFolderPath('Desktop')
if (-not $desk) { $desk = Join-Path $env:USERPROFILE 'Desktop' }
$out = Join-Path $desk ('HTF-transfer-' + $stamp)
[void](New-Item -ItemType Directory -Force (Join-Path $out 'Saves'))
[void](New-Item -ItemType Directory -Force (Join-Path $out 'fortress-vault'))
[void](New-Item -ItemType Directory -Force (Join-Path $out 'fortress-backups'))

Copy-Item (Join-Path $saves '*') (Join-Path $out 'Saves') -Recurse -Force
# The sender's own local.txt is per machine: never overwrite the receiver's with it.
Get-ChildItem (Join-Path $out 'Saves') -Recurse -File -Filter 'local.txt*' -ErrorAction SilentlyContinue |
    Remove-Item -Force -ErrorAction SilentlyContinue

$vault = Join-Path $game 'BepInEx\fortress-vault'
if (Test-Path $vault) { Copy-Item (Join-Path $vault '*') (Join-Path $out 'fortress-vault') -Recurse -Force }
$backs = Join-Path $game 'BepInEx\fortress-backups'
if (Test-Path $backs) { Copy-Item (Join-Path $backs '*') (Join-Path $out 'fortress-backups') -Recurse -Force }

$man = New-Object System.Collections.Generic.List[string]
$man.Add('OVER 99 host move - collected ' + $stamp)
$man.Add('source game: ' + $game)
$man.Add('')
$man.Add('saves:')
foreach ($f in Get-ChildItem (Join-Path $out 'Saves') -Recurse -File) {
    $man.Add('  ' + $f.Name + '  ' + $f.Length + ' bytes')
}
$man.Add('')
$man.Add('vault (own lines = kinds of gear in it; scales = the team currency):')
foreach ($f in Get-ChildItem (Join-Path $out 'fortress-vault') -File) {
    $own = @(Select-String -Path $f.FullName -Pattern '^own ' -ErrorAction SilentlyContinue).Count
    $line = Select-String -Path $f.FullName -Pattern '^scales ' -ErrorAction SilentlyContinue
    $sc = ''
    if ($line) { $sc = '  ' + $line.Line }
    $man.Add('  ' + $f.Name + '  ' + $f.Length + ' bytes  own lines: ' + $own + $sc)
}
$man.Add('')
$man.Add('Send this ZIP to the new host and run install-save-mac.command there.')
Set-Content -Path (Join-Path $out 'manifest.txt') -Value $man -Encoding UTF8

$zip = $out + '.zip'
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $out -Recurse -Force

Write-Host ''
Write-Host ('DONE: ' + $zip) -ForegroundColor Green
Start-Process explorer ('/select,' + $zip)
exit 0
