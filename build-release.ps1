# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Jouko Broström
# Bygger en release av Cyberdrome Cockpit:
#   tester -> licenslista -> fristående publicering (win-x64) -> Velopack-paket (Setup.exe, uppdateringar)
# Resultat:
#   ..\release\<version>\        Setup.exe, MSI, portabel zip, licenstexter
#   ..\release\uppdateringar\    alla versioner (Velopack-flöde som programmet söker uppdateringar i)
# Användning:  powershell -ExecutionPolicy Bypass -File build-release.ps1 [-Version 1.0.1] [-SkipTests] [-NoMsi] [-Sign none|test|certificate|azure]
# Skriptet laddar inte upp något; GitHub Actions (release.yml) laddar upp till GitHub Releases vid en tagg v*.
# Kodsignering styrs av signing.json bredvid skriptet (se signing.example.json och README: Kodsignering).
# signing.json innehåller bara tumavtryck och sökvägar - aldrig lösenord eller nycklar.
param(
    [string]$Version = "1.2.0",
    [switch]$SkipTests,
    [switch]$NoMsi,
    [ValidateSet("", "none", "test", "certificate", "azure")][string]$Sign = "",
    # Var releasen hamnar (standard: mappen release bredvid kod\). GitHub Actions anger en mapp i arbetskatalogen.
    [string]$OutRoot = "",
    # Releaseanteckningar (Markdown) som bäddas in i paketet; visas i programmet och på GitHub-releasen.
    [string]$ReleaseNotes = ""
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$app = Join-Path $root "src\Cyberdrome.App\Cyberdrome.App.csproj"
$releaseRoot = if ($OutRoot) { $OutRoot } else { Join-Path (Split-Path $root -Parent) "release" }   # standard: bredvid kod\
$feed = Join-Path $releaseRoot "uppdateringar"
$outDir = Join-Path $releaseRoot $Version
$publish = Join-Path $root "publish\$Version"
$packId = "CyberdromeSimulator"   # installeras i %LocalAppData%\CyberdromeSimulator (egna data ligger i %AppData%\Cyberdrome)

function Step($t) { Write-Host ""; Write-Host ">> $t" -ForegroundColor Cyan }

# ---------------- Kodsignering ----------------
$signCfg = [pscustomobject]@{ mode = "none"; timestampUrl = "http://timestamp.digicert.com" }
$signFile = Join-Path $root "signing.json"
if (Test-Path $signFile) {
    $loaded = [IO.File]::ReadAllText($signFile, [Text.Encoding]::UTF8) | ConvertFrom-Json
    foreach ($p in $loaded.PSObject.Properties) { $signCfg | Add-Member -NotePropertyName $p.Name -NotePropertyValue $p.Value -Force }
}
if ($Sign) { $signCfg.mode = $Sign }
$testSubject = "CN=Cyberdrome Test (Jouko Broström)"

function Get-TestCertificate {
    # Självsignerat testcertifikat i användarens personliga certifikatarkiv. Det är INTE betrott av andra datorer
    # och tas inte in i betrodda arkiv av skriptet. Nyckeln går inte att exportera.
    $c = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object { $_.Subject -eq $testSubject -and $_.NotAfter -gt (Get-Date).AddDays(7) } | Select-Object -First 1
    if (-not $c) {
        Write-Host "Skapar testcertifikat '$testSubject' i CurrentUser\My"
        $c = New-SelfSignedCertificate -Type CodeSigningCert -Subject $testSubject -CertStoreLocation Cert:\CurrentUser\My `
            -KeyExportPolicy NonExportable -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(2) `
            -FriendlyName "Cyberdrome testsignering (ej betrott)"
    }
    return $c
}

$signArgs = @()
$signDesc = "osignerad"
switch ($signCfg.mode) {
    "none" { }
    "test" {
        $cert = Get-TestCertificate
        $signArgs = @("--signParams", "/fd SHA256 /sha1 $($cert.Thumbprint) /tr $($signCfg.timestampUrl) /td SHA256")
        $signDesc = "testcertifikat $($cert.Thumbprint) (ej betrott)"
    }
    "certificate" {
        if ($signCfg.signTemplate) {
            # T.ex. SSL.com eSigner CodeSignTool: inloggning via miljövariabler, aldrig i filen.
            $signArgs = @("--signTemplate", $signCfg.signTemplate)
            $signDesc = "eget signeringskommando"
        }
        else {
            if (-not $signCfg.certificateThumbprint) { throw "signing.json: certificateThumbprint saknas (läget certificate)." }
            $thumb = ($signCfg.certificateThumbprint -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
            $cert = Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My | Where-Object Thumbprint -eq $thumb | Select-Object -First 1
            if (-not $cert) { throw "Certifikatet $thumb finns inte i certifikatarkivet (är USB-nyckeln ansluten?)." }
            $extra = if ($signCfg.signtoolExtra) { " $($signCfg.signtoolExtra)" } else { "" }
            $signArgs = @("--signParams", "/fd SHA256 /sha1 $thumb /tr $($signCfg.timestampUrl) /td SHA256$extra")
            $signDesc = "certifikat $($cert.Subject)"
        }
    }
    "azure" {
        if (-not $signCfg.azureMetadataFile -or -not (Test-Path $signCfg.azureMetadataFile)) { throw "signing.json: azureMetadataFile saknas eller finns inte (läget azure)." }
        $signArgs = @("--azureTrustedSignFile", $signCfg.azureMetadataFile)
        $signDesc = "Azure Artifact Signing"
    }
    default { throw "Okänt signeringsläge: $($signCfg.mode)" }
}
Write-Host "Kodsignering: $signDesc"

Step "Kontrollerar egna Android-appar"
foreach ($apk in "android\CyberdromeLauncher\bin\CyberdromeLauncher.apk", "android\CyberdromeRadio\bin\CyberdromeRadio.apk") {
    if (-not (Test-Path (Join-Path $root $apk))) { throw "$apk saknas - bygg den med android\...\build.ps1 forst." }
}

Step "Licenslista (THIRD-PARTY-NOTICES.txt)"
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root "tools\gen-notices.ps1") -Version $Version
if ($LASTEXITCODE -ne 0) { throw "gen-notices misslyckades" }

if (-not $SkipTests) {
    Step "Tester"
    & dotnet test (Join-Path $root "tests\Cyberdrome.Tests") -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Testerna misslyckades - ingen release byggs." }
}

Step "Publicerar $Version (fristaende, win-x64)"
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
& dotnet publish $app -c Release -r win-x64 --self-contained true -p:Version=$Version -p:DebugType=none -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish misslyckades" }

Step "Kontrollerar innehallet (bara egna filer och appar)"
$apps = Get-ChildItem (Join-Path $publish "apps") -Filter *.apk | ForEach-Object Name
$allowed = @("CyberdromeLauncher.apk", "CyberdromeRadio.apk")
$extra = $apps | Where-Object { $allowed -notcontains $_ }
if ($extra) { throw "Otillatna appar i paketet: $extra" }
$forbidden = Get-ChildItem $publish -Recurse -File | Where-Object { $_.Extension -in ".img", ".qcow2", ".iso" -or $_.Name -match "(?i)androbd|organicmaps|obdb|bildatabas|system-images" }
if ($forbidden) { throw "Otillatna filer i paketet: $($forbidden.Name -join ', ')" }
Write-Host "Appar: $($apps -join ', ')"

Step "Kontrollerar produktnamn och version i vara egna filer"
# SignPath kraver fasta metadata: produktnamn = projektets namn och samma produktversion i varje bygge.
$own = Get-ChildItem $publish -File | Where-Object { $_.Name -like "Cyberdrome*.dll" -or $_.Name -eq "Cyberdrome.exe" }
foreach ($f in $own) {
    $vi = [Diagnostics.FileVersionInfo]::GetVersionInfo($f.FullName)
    $pv = ($vi.ProductVersion -split '\+')[0]
    if ($vi.ProductName -ne "Cyberdrome Cockpit" -or $pv -ne $Version) {
        throw "$($f.Name): produktnamn '$($vi.ProductName)' och version '$pv' (ska vara 'Cyberdrome Cockpit' och '$Version')."
    }
}
Write-Host "$($own.Count) filer: Cyberdrome Cockpit $Version"

Step "Velopack-paket"
New-Item -ItemType Directory -Force $feed, $outDir | Out-Null
# Installationsfiler skrivs om per version (MSI byggs bara när den inte är avstängd).
Get-ChildItem $feed -File | Where-Object { $_.Name -like "*Setup*" -or $_.Name -like "*Portable*" -or $_.Name -like "*.msi" } | Remove-Item -Force
$vpkArgs = @("pack", "-u", $packId, "-v", $Version, "-p", $publish, "-e", "Cyberdrome.exe",
    "--packTitle", "Cyberdrome Cockpit", "--packAuthors", "Jouko Broström",
    "--icon", (Join-Path $root "src\Cyberdrome.App\Assets\cyberdrome.ico"),
    "--shortcuts", "Desktop,StartMenuRoot", "-r", "win-x64", "-o", $feed)
# MSI-guiden visar GPL-licensen (vpk vill ha .md eller .rtf).
$licenseMd = Join-Path $env:TEMP "cyberdrome-LICENSE.md"
Copy-Item (Join-Path $root "LICENSE") $licenseMd -Force
if (-not $NoMsi) { $vpkArgs += @("--msi", "--instLicense", $licenseMd) }
if ($ReleaseNotes) { $vpkArgs += @("--releaseNotes", (Resolve-Path $ReleaseNotes).Path) }
$vpkArgs += $signArgs
& vpk @vpkArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack misslyckades" }

if ($signCfg.mode -ne "none") {
    Step "Kontrollerar signaturerna"
    # Programfilerna kontrolleras i det färdiga paketet (vpk signerar en kopia, inte publiceringsmappen).
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "cd-signcheck-$Version"
    if (Test-Path $tmp) { Remove-Item $tmp -Recurse -Force }
    New-Item -ItemType Directory $tmp | Out-Null
    $zip = [IO.Compression.ZipFile]::OpenRead((Join-Path $feed "$packId-$Version-full.nupkg"))
    try {
        foreach ($name in "Cyberdrome.exe", "Cyberdrome.Core.dll", "Cyberdrome.Obd.dll") {
            $e = $zip.Entries | Where-Object { $_.Name -eq $name } | Select-Object -First 1
            if ($e) { [IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $tmp $name), $true) }
        }
    }
    finally { $zip.Dispose() }
    $check = @(Get-ChildItem $feed -File | Where-Object { $_.Name -like "*Setup.exe" -or $_.Name -like "*.msi" }) + @(Get-ChildItem $tmp -File)
    foreach ($f in $check) {
        $s = Get-AuthenticodeSignature $f.FullName
        $who = if ($s.SignerCertificate) { $s.SignerCertificate.Subject } else { "-" }
        $ts = if ($s.TimeStamperCertificate) { "tidsstämplad" } else { "ingen tidsstämpel" }
        Write-Host ("{0,-40} {1,-14} {2} ({3})" -f $f.Name, $s.Status, $who, $ts)
        if (-not $s.SignerCertificate) { throw "$($f.Name) är inte signerad." }
        if ($signCfg.mode -ne "test" -and $s.Status -ne "Valid") { throw "$($f.Name): signaturen är inte giltig ($($s.Status))." }
    }
    if ($signCfg.mode -eq "test") { Write-Host "Testcertifikatet är inte betrott: status 'UnknownError'/'NotTrusted' är väntat." -ForegroundColor Yellow }
}

Step "Kopierar till $outDir"
Get-ChildItem $feed -File | Where-Object { $_.Name -like "*Setup*" -or $_.Name -like "*Portable*" -or $_.Name -like "*.msi" } |
    Copy-Item -Destination $outDir -Force
Copy-Item (Join-Path $root "LICENSE"), (Join-Path $root "legal\DISCLAIMER.md"), (Join-Path $root "legal\THIRD-PARTY-NOTICES.txt") -Destination $outDir -Force
Get-ChildItem $outDir | Format-Table Name, Length -AutoSize
Write-Host "Klart: $outDir" -ForegroundColor Green
