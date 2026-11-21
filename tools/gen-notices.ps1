# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Jouko Broström
# Generates legal\THIRD-PARTY-NOTICES.txt from the NuGet packages that Cyberdrome.App uses (incl. transitive)
# and their licence metadata (.nuspec), plus the fixed text in legal\notices-template.txt (UTF-8).
# Usage: powershell -ExecutionPolicy Bypass -File tools\gen-notices.ps1 [-Version 1.0.0]
param([string]$Version = "1.0.0")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$utf8 = New-Object System.Text.UTF8Encoding($true)

$out = & dotnet list (Join-Path $root "src\Cyberdrome.App\Cyberdrome.App.csproj") package --include-transitive
$pk = @{}
foreach ($l in $out) { if ($l -match '^\s+>\s+(\S+)\s+(?:\S+\s+)?(\d+\.\d+\S*)\s*$') { $pk[$Matches[1]] = $Matches[2] } }
# Build-time / debug-only packages are not shipped.
$skip = @('Avalonia.Diagnostics', 'Avalonia.BuildServices')
$fix = @{ 'Avalonia.Angle.Windows.Natives' = 'BSD-3-Clause (ANGLE)'; 'SQLite' = 'Public domain (SQLite)' }
$cache = Join-Path $env:USERPROFILE ".nuget\packages"
$lines = foreach ($k in ($pk.Keys | Sort-Object)) {
    if ($skip -contains $k) { continue }
    $v = $pk[$k]
    $ns = Get-ChildItem (Join-Path $cache "$($k.ToLower())\$v") -Filter *.nuspec -ErrorAction SilentlyContinue | Select-Object -First 1
    $lic = ""; $auth = ""; $url = ""
    if ($ns) {
        [xml]$x = [IO.File]::ReadAllText($ns.FullName)
        $m = $x.package.metadata
        if ($m.license) { $lic = if ($m.license.'#text') { $m.license.'#text' } else { "$($m.license)" } }
        elseif ($m.licenseUrl) { $lic = "see " + $m.licenseUrl }
        $auth = $m.authors; $url = ($m.projectUrl -replace '\?utm.*', '')
    }
    if ($fix.ContainsKey($k)) { $lic = $fix[$k] }
    $tail = if ($url) { " - $url" } else { "" }
    "$k $v`r`n    Licens: $lic - $auth$tail"
}
$tpl = [IO.File]::ReadAllText((Join-Path $root "legal\notices-template.txt"), [Text.Encoding]::UTF8)
$text = $tpl.Replace("{{VERSION}}", $Version).Replace("{{PACKAGES}}", ($lines -join "`r`n"))
[IO.File]::WriteAllText((Join-Path $root "legal\THIRD-PARTY-NOTICES.txt"), $text, $utf8)
Write-Host "THIRD-PARTY-NOTICES.txt: $($lines.Count) paket"
