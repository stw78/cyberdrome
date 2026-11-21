# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Jouko Broström
# Bygger appen i den här mappen (APK) utan Gradle, med verktygen i Android SDK.
# Användning:  powershell -ExecutionPolicy Bypass -File build.ps1  [-Sdk C:\CyberdromeSDK]
# Resultat:    bin\<mappnamn>.apk
param(
    [string]$Sdk = "C:\CyberdromeSDK",
    [string]$JdkPath = "",   # valfritt: JDK-mapp (t.ex. $env:JAVA_HOME i GitHub Actions); annars $Sdk\jdk
    [string]$BuildTools = "34.0.0",
    [string]$Platform = "android-34"
)
$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$apkName = (Split-Path $here -Leaf) + ".apk"
$bt = Join-Path $Sdk "build-tools\$BuildTools"
$androidJar = Join-Path $Sdk "platforms\$Platform\android.jar"
$jdk = if ($JdkPath) { Get-Item $JdkPath } else { Get-ChildItem (Join-Path $Sdk "jdk") -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName "bin\javac.exe") } | Select-Object -First 1 }
if (-not $jdk) { throw "Hittar ingen JDK i $Sdk\jdk" }
$env:JAVA_HOME = $jdk.FullName
$javac = Join-Path $jdk.FullName "bin\javac.exe"
$keytool = Join-Path $jdk.FullName "bin\keytool.exe"
foreach ($p in @($androidJar, (Join-Path $bt "aapt2.exe"), (Join-Path $bt "d8.bat"), (Join-Path $bt "apksigner.bat"), (Join-Path $bt "zipalign.exe"))) {
    if (-not (Test-Path $p)) { throw "Saknas: $p  (installera med: sdkmanager `"platforms;$Platform`" `"build-tools;$BuildTools`")" }
}

$out = Join-Path $here "bin"
$tmp = Join-Path $here "obj"
Remove-Item $out, $tmp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out, "$tmp\classes", "$tmp\dex" | Out-Null

function Invoke-Tool($exe, [string[]]$toolArgs) {
    & $exe @toolArgs
    if ($LASTEXITCODE -ne 0) { throw "$([IO.Path]::GetFileName($exe)) misslyckades (kod $LASTEXITCODE)" }
}

Write-Host ">> 1/5 Manifest (aapt2)"
Invoke-Tool (Join-Path $bt "aapt2.exe") @("link", "-o", "$tmp\base.apk", "--manifest", "$here\AndroidManifest.xml", "-I", $androidJar)

Write-Host ">> 2/5 Kompilerar Java (javac)"
$sources = Get-ChildItem "$here\src" -Recurse -Filter *.java | ForEach-Object { $_.FullName }
# core-lambda-stubs.jar (från build-tools) behövs för lambda-uttryck när android.jar är bootclasspath.
$boot = "$androidJar;$(Join-Path $bt 'core-lambda-stubs.jar')"
Invoke-Tool $javac (@("-encoding", "UTF-8", "-source", "8", "-target", "8", "-Xlint:-options", "-bootclasspath", $boot, "-d", "$tmp\classes") + $sources)

Write-Host ">> 3/5 Dex (d8)"
$classes = Get-ChildItem "$tmp\classes" -Recurse -Filter *.class | ForEach-Object { $_.FullName }
Invoke-Tool (Join-Path $bt "d8.bat") (@("--min-api", "29", "--lib", $androidJar, "--output", "$tmp\dex") + $classes)

Write-Host ">> 4/5 Packar och justerar (zipalign)"
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open("$tmp\base.apk", "Update")
[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$tmp\dex\classes.dex", "classes.dex") | Out-Null
$zip.Dispose()
Invoke-Tool (Join-Path $bt "zipalign.exe") @("-f", "4", "$tmp\base.apk", "$tmp\aligned.apk")

Write-Host ">> 5/5 Signerar (lokal debug-nyckel)"
$ks = Join-Path $env:LOCALAPPDATA "Cyberdrome\debug.keystore"
if (-not (Test-Path $ks)) {
    New-Item -ItemType Directory -Force (Split-Path $ks) | Out-Null
    # Standardlösenordet "android" är Androids vanliga konvention för debug-nycklar (bara för test).
    Invoke-Tool $keytool @("-genkeypair", "-keystore", $ks, "-storepass", "android", "-keypass", "android", "-alias", "androiddebugkey",
        "-keyalg", "RSA", "-keysize", "2048", "-validity", "10000", "-dname", "CN=Cyberdrome Debug,O=Cyberdrome,C=SE")
}
Invoke-Tool (Join-Path $bt "apksigner.bat") @("sign", "--ks", $ks, "--ks-pass", "pass:android", "--key-pass", "pass:android",
    "--out", "$out\$apkName", "$tmp\aligned.apk")

Write-Host ">> Klart: $out\$apkName"
