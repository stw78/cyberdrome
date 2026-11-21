# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Jouko Brostrom
# Plockar ut avsnittet "## <version>" ur CHANGELOG.md och skriver det till en fil (releaseanteckningar).
# Anvandning: powershell -File tools\changelog-section.ps1 -Version 1.1.0 -Out release-notes.md
param([Parameter(Mandatory)][string]$Version, [Parameter(Mandatory)][string]$Out)
$ErrorActionPreference = "Stop"
$log = Join-Path (Split-Path $PSScriptRoot -Parent) "CHANGELOG.md"
$lines = [IO.File]::ReadAllLines($log, [Text.Encoding]::UTF8)
$start = [Array]::FindIndex($lines, [Predicate[string]] { param($l) $l.Trim() -eq "## $Version" })
if ($start -lt 0) { throw "CHANGELOG.md saknar avsnittet '## $Version'." }
$end = $lines.Length
for ($i = $start + 1; $i -lt $lines.Length; $i++) { if ($lines[$i] -match '^## ') { $end = $i; break } }
$body = ($lines[($start + 1)..($end - 1)] -join "`n").Trim()
if (-not $body) { throw "Avsnittet '## $Version' i CHANGELOG.md ar tomt." }
[IO.File]::WriteAllText($Out, "## Cyberdrome Cockpit $Version`n`n$body`n", (New-Object Text.UTF8Encoding($false)))
Write-Host "Releaseanteckningar for $Version -> $Out"
