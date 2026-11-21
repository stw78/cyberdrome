# Cyberdrome Simulator

**English** · [Svenska](README.sv.md)

Cyberdrome simulates an Android car radio with a virtual OBD port, so you can develop and test media, radio and OBD
apps without a real car. It runs Android Automotive, emulates car models with their own themes, trouble codes, coding
and GPS, and can also connect to a real OBD adapter.

- **Free software:** GPL-3.0-or-later (see [LICENSE](LICENSE)) – © 2026 Jouko Broström. No warranty, see the
  [disclaimer](legal/DISCLAIMER.md).
- **Support the development:** Cyberdrome is free and funded by donations – [donate via PayPal](https://www.paypal.me/stw78).
- **Contribute:** see [CONTRIBUTING.md](CONTRIBUTING.md).

The program's user interface is currently in Swedish; an English version is in progress. The full documentation is in
[README.sv.md](README.sv.md).

## Features (version 1.0)

| Part | Status |
|---|---|
| Virtual ELM327 over Wi-Fi (TCP 35000), Bluetooth and Bluetooth LE | Done |
| OBD 01/03/04/09, UDS 10/22/2E/27/3E, manufacturer signals (21/22) | Done |
| Android Automotive inside the program (no restart), themes and skins per car model | Done |
| Car models: own models, sharing (.cbs), ODX/VSS/CSV, car database (NHTSA vPIC, Wikidata, OBDb) | Done |
| Drives, map and GPS to Android | Done |
| Test cases with reports | Done |
| Real OBD adapter: read-only by default, bridge to Android, write protection | Done (tested against a fake adapter) |
| Installer and updates (Velopack) | Done |

## Build and run

Requirements: Windows 10/11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone <repository-url>
cd <repository>
dotnet build
dotnet test
dotnet run --project src/Cyberdrome.App
```

The first time, Windows Firewall asks whether the program may use the network. Answer **Yes (private network)**,
otherwise apps cannot connect to the virtual OBD port (TCP 35000).

The Android emulator, Android system images (including Google APIs images), AndrOBD, Organic Maps, map tiles and car
database data are **not** part of the repository. The user downloads them from inside the program.

The two Android apps (Cyberdrome Launcher and Cyberdrome Radio) are built without Gradle by `android\*\build.ps1`
(needs the Android SDK build tools, which the program can install).

## Release

```powershell
powershell -ExecutionPolicy Bypass -File build-release.ps1 -Version 1.0.0
```

The script runs the tests and generates `legal\THIRD-PARTY-NOTICES.txt`. It publishes a self-contained win-x64 build
and checks that no third-party apps or images are included. It then packs Setup.exe, an MSI, a portable zip and update
packages with Velopack into `..\release\`.

Code signing is configured in `signing.json` (template: `signing.example.json`; never committed). The modes are
`test` (self-signed, not trusted), `certificate` and `azure`. Signing of the public builds is planned through
**SignPath Foundation** (free for open source) in GitHub Actions (`.github/workflows/build.yml`).

## Licence and credits

Cyberdrome is licensed under GPL-3.0-or-later. It uses open-source components (Avalonia, CommunityToolkit.Mvvm,
Velopack, SQLite, SkiaSharp, .NET and others) under their own licences. See
[legal/THIRD-PARTY-NOTICES.txt](legal/THIRD-PARTY-NOTICES.txt). Data downloaded at runtime comes from these sources:

- maps © OpenStreetMap contributors (ODbL)
- OBDb (CC BY-SA 4.0)
- Wikidata (CC0)
- NHTSA vPIC

No manufacturer logos, fonts or security algorithms are included.
