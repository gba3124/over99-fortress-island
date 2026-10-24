# OVER 99 Fortress Island - move host (part 2: install, receiver / new host, Windows)
# Embedded in install-save-windows.bat (base64, decoded with certutil at run time) so the receiver only needs ONE file.
# It takes HTF-transfer-*.zip from the old host, backs up the existing save/vault, then installs the save + vault.
$ErrorActionPreference = 'Stop'

function Resolve-Game([string]$p) {
    if ([string]::IsNullOrWhiteSpace($p)) { return $null }
    $p = $p.Trim().Trim('"').TrimEnd('\')
    if (Test-Path (Join-Path $p 'How to Fish.exe')) { return $p }
    $child = Join-Path $p 'How to Fish'
    if (Test-Path (Join-Path $child 'How to Fish.exe')) { return $child }
    return $null
}

function Find-Game {
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
    foreach ($c in $candidates) { $g = Resolve-Game $c; if ($g) { return $g } }
    return $null
}

function SteamId64([string]$steamPath) {
    if (-not $steamPath) { return $null }
    $vdf = Join-Path $steamPath 'config\loginusers.vdf'
    if (-not (Test-Path $vdf)) { return $null }
    $text = Get-Content $vdf -Raw
    $m = [regex]::Match($text, '"(\d{17})"\s*\{[^}]*"MostRecent"\s+"1"', 'Singleline')
    if ($m.Success) { return $m.Groups[1].Value }
    $m = [regex]::Match($text, '"(\d{17})"')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

Write-Host '=== OVER 99 move host: install save + vault (new host, Windows) ==='
Write-Host ''

$game = Find-Game
if (-not $game) {
    Write-Host 'The game folder was not found automatically.' -ForegroundColor Yellow
    Write-Host 'Find it with: Steam -> right click How to Fish -> Manage -> Browse local files' -ForegroundColor Yellow
    $typed = Read-Host 'Paste that folder here (the one with How to Fish.exe), or press Enter to quit'
    if ($typed) { $game = Resolve-Game $typed }
}
if (-not $game) { Write-Host 'FAIL: game folder not found.' -ForegroundColor Red; exit 1 }
Write-Host ('Game folder: ' + $game)

# The transfer zip: argument -> newest on the Desktop -> ask
$desk = [Environment]::GetFolderPath('Desktop')
if (-not $desk) { $desk = Join-Path $env:USERPROFILE 'Desktop' }
$zip = $null
if ($args.Count -ge 1 -and $args[0]) { $zip = $args[0].Trim().Trim('"') }
if (-not $zip -or -not (Test-Path $zip)) {
    $found = Get-ChildItem $desk -Filter 'HTF-transfer-*.zip' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending
    if ($found -and $found.Count -gt 0) { $zip = $found[0].FullName }
}
if (-not $zip -or -not (Test-Path $zip)) {
    Write-Host 'No HTF-transfer-*.zip found on the Desktop.' -ForegroundColor Yellow
    $zip = Read-Host 'Paste the full path of the zip the old host sent you'
    $zip = $zip.Trim().Trim('"')
}
if (-not $zip -or -not (Test-Path $zip)) { Write-Host 'FAIL: transfer zip not found.' -ForegroundColor Red; exit 1 }
Write-Host ('Transfer zip: ' + $zip)

if (Get-Process -Name 'How to Fish' -ErrorAction SilentlyContinue) {
    Write-Host ''
    Write-Host 'The game is running: close How to Fish completely, then run this again.' -ForegroundColor Red
    exit 1
}

$saves = Join-Path $env:USERPROFILE 'AppData\LocalLow\Dazed Games\How to Fish\Saves'
$vault = Join-Path $game 'BepInEx\fortress-vault'
$backs = Join-Path $game 'BepInEx\fortress-backups'

# Which SteamID folder is mine? loginusers.vdf's most recent account, else the folder with local.txt, else ask.
$sp = $null
try { $sp = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath } catch { }
$sid = SteamId64 $sp
if (-not $sid -and (Test-Path $saves)) {
    foreach ($d in Get-ChildItem $saves -Directory) {
        if (Test-Path (Join-Path $d.FullName 'local.txt')) { $sid = $d.Name; break }
    }
}
if (-not $sid) {
    Write-Host 'Could not read your Steam ID.' -ForegroundColor Yellow
    $sid = Read-Host 'Paste the folder name under ...\How to Fish\Saves (a long number), or Enter for 76561198009466774'
    if (-not $sid) { $sid = '76561198009466774' }
}
$dest = Join-Path $saves $sid
Write-Host ('Save folder: ' + $dest)

$tmp = Join-Path $env:TEMP ('htf-install-' + [guid]::NewGuid().ToString('N'))
Expand-Archive -Path $zip -DestinationPath $tmp -Force
if (-not (Test-Path (Join-Path $tmp 'Saves'))) {
    Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host 'FAIL: this zip has no Saves folder (not made by the collector?).' -ForegroundColor Red
    exit 1
}

# Back up what this machine has now, on the Desktop.
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$bk = Join-Path $desk ('HTF-hostmove-backup-' + $stamp)
[void](New-Item -ItemType Directory -Force $bk)
if (Test-Path $saves) { Copy-Item $saves (Join-Path $bk 'Saves') -Recurse -Force }
if (Test-Path $vault) { Copy-Item $vault (Join-Path $bk 'fortress-vault') -Recurse -Force }
if (Test-Path $backs) { Copy-Item $backs (Join-Path $bk 'fortress-backups') -Recurse -Force }
Write-Host ('Backup of the current save/vault: ' + $bk)

[void](New-Item -ItemType Directory -Force $dest)
[void](New-Item -ItemType Directory -Force $vault)
[void](New-Item -ItemType Directory -Force $backs)

$nSaves = 0
foreach ($f in Get-ChildItem (Join-Path $tmp 'Saves') -Recurse -File) {
    if ($f.Extension -notin @('.txt', '.backup')) { continue }
    if ($f.Name -like 'local.txt*') { continue }   # each machine's own player file
    Copy-Item $f.FullName (Join-Path $dest $f.Name) -Force
    $nSaves++
}
$nVault = 0
if (Test-Path (Join-Path $tmp 'fortress-vault')) {
    foreach ($f in Get-ChildItem (Join-Path $tmp 'fortress-vault') -File) {
        Copy-Item $f.FullName (Join-Path $vault $f.Name) -Force
        $nVault++
    }
}
$nBacks = 0
if (Test-Path (Join-Path $tmp 'fortress-backups')) {
    foreach ($f in Get-ChildItem (Join-Path $tmp 'fortress-backups') -File) {
        Copy-Item $f.FullName (Join-Path $backs $f.Name) -Force
        $nBacks++
    }
}
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue

Write-Host ''
Write-Host ('Installed: saves ' + $nSaves + ', vault files ' + $nVault + ', backups ' + $nBacks)
Write-Host ''
Write-Host 'Saves:'
foreach ($f in (Get-ChildItem $dest -File | Sort-Object LastWriteTime -Descending)) { Write-Host ('  ' + $f.Name) }
Write-Host ''
Write-Host 'Vault (name / size / scales / kinds of gear):'
foreach ($f in (Get-ChildItem $vault -File -Filter '*.txt')) {
    $own = @(Select-String -Path $f.FullName -Pattern '^own ' -ErrorAction SilentlyContinue).Count
    $line = Select-String -Path $f.FullName -Pattern '^scales ' -ErrorAction SilentlyContinue
    $sc = '?'
    if ($line) { $sc = ($line.Line -split ' ')[1] }
    Write-Host ('  ' + $f.Name + '  ' + $f.Length + ' bytes  scales ' + $sc + '  gear kinds ' + $own)
}

Write-Host ''
Write-Host 'Next:'
Write-Host '  1. Start the game, load the save just installed, and host it.'
Write-Host '  2. Everyone must run the same mod version (0.1.3-a64b5c), or a version warning appears.'
Write-Host '  3. To undo: copy the files from the backup folder back.'
Write-Host ''
Write-Host 'Press Enter to close.'
[void](Read-Host)
exit 0
