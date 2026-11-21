# Cyberdrome Simulator

[English](README.md) · **Svenska**

Cyberdrome simulerar en Android-bilradio med en virtuell OBD-port, så att du kan utveckla och testa media-, radio- och
OBD-appar utan en riktig bil. Programmet kör Android Automotive, emulerar bilmodeller med egna teman, felkoder, kodning
och GPS, och kan även ansluta en riktig OBD-adapter.

- **Fri programvara:** GPL-3.0-or-later (se [LICENSE](LICENSE)) – © 2026 Jouko Broström. Ingen garanti, se
  [ansvarsfriskrivningen](legal/DISCLAIMER.md).
- **Stöd utvecklingen:** Cyberdrome är gratis och drivs av donationer – [donera via PayPal](https://www.paypal.me/stw78).
- **Bidra:** se [CONTRIBUTING.md](CONTRIBUTING.md).
## Kom igång (Windows + VS Code)

1. Hämta koden (`git clone`) och öppna mappen i VS Code (Arkiv → Öppna mapp). Du behöver .NET 10 SDK på Windows 10/11.
2. Installera tillägget **C# Dev Kit** om VS Code frågar.
3. Tryck **F5** (eller kör `dotnet run --project src/Cyberdrome.App` i terminalen).
4. Första gången frågar Windows-brandväggen om programmet får använda nätverket. Svara **Ja (privat nätverk)**, annars kan inga appar ansluta till den virtuella OBD-porten.

Kör testerna: `dotnet test`

## Installation och release (version 1.0)

**Installera:** kör `CyberdromeSimulator-win-Setup.exe`. Den installerar per användare i
`%LocalAppData%\CyberdromeSimulator`, utan administratörsbehörighet, och skapar genvägar på skrivbordet och i Start-menyn.
Licensavtalet visas och måste godkännas vid första starten. Alternativt finns `CyberdromeSimulator-win.msi`, som visar
licensavtalet i installationsguiden. MSI-varianten installerar för hela datorn och kräver administratör.

**Avinstallera:** via Inställningar → Appar i Windows. Programmet frågar då **"Vill du även ta bort mina data?"**.
Standardsvaret är **Nej**: egna bilmodeller, teman, skal, körningar, testfall, säkerhetskopior och inställningar i
`%AppData%\Cyberdrome` samt loggar och cache i `%LocalAppData%\Cyberdrome` finns kvar.

**Paketet innehåller** bara egna filer: programmet med .NET (fristående), bilmodellerna, licenstexterna och våra egna
Android-appar (Cyberdrome Launcher och Cyberdrome Radio i `apps\`). Följande hämtas av användaren när programmet körs och
följer inte med: Android-emulatorn och Google-avbildningar, AndrOBD, Organic Maps, kartrutor, vPIC-, Wikidata- och OBDb-data.

**Uppdateringar:** Inställningar → Uppdateringar söker i en lokal mapp, som standard
`Skrivbord\Projekt\release\uppdateringar` om mappen `Skrivbord\Projekt` finns, annars `Dokument\Cyberdrome\release\uppdateringar`. Ingen server används. "Installera uppdatering" hämtar, stänger programmet,
installerar och startar igen. Dina data påverkas inte.

**Bygga en release:** `powershell -ExecutionPolicy Bypass -File build-release.ps1 -Version 1.0.0`. Skriptet:
- kör testerna och genererar `legal\THIRD-PARTY-NOTICES.txt` från NuGet-paketen (`tools\gen-notices.ps1`),
- publicerar fristående för win-x64 och kontrollerar att inga otillåtna appar eller filer följer med,
- packar med Velopack (`vpk`, MIT).

Resultat: `..\release\<version>` (bredvid kodmappen) (Setup.exe, MSI, portabel zip, licenser) och flödet
`release\uppdateringar`. Flaggor: `-SkipTests`, `-NoMsi`. Ikonen ritas med `tools\make-icon.ps1`.

**Kodsignering.** Utan signatur varnar Windows SmartScreen ("Okänd utgivare"). Byggskriptet kan signera allt (programmet,
egna DLL:er, Setup.exe och MSI) med tidsstämpel. Läget väljs i `signing.json` (mall: `signing.example.json`, filen
checkas aldrig in):
- `test`: självsignerat testcertifikat i ditt certifikatarkiv. Det är bara för att prova kedjan och är inte betrott.
- `certificate`: eget köpt certifikat (tumavtryck, t.ex. på USB-nyckel) eller ett molnsigneringskommando (`signTemplate`).
- `azure`: Azure Artifact Signing (`metadata.json`).

För den öppna versionen är planen **SignPath Foundation**, som signerar gratis för öppen källkod. Det kräver ett publikt
GitHub-repo och ett bygge i GitHub Actions; arbetsflödet finns i `.github/workflows/build.yml` med ett förberett
signeringssteg.

**Licenser:** knapparna **Om** och **Licens** i toppfältet visar version, beskrivning, ansvarsfriskrivningen, GPL-3.0 och
tredjepartslicenserna. Ansvarsfriskrivningen visas också vid första starten.
## Prova den virtuella OBD-porten

Programmet startar en låtsas Wi-Fi OBD-dongel (ELM327) på port **35000**.
Adressen visas i fönstret, t.ex. `192.168.1.20:35000`.

- **Android-telefon på samma Wi-Fi:** öppna en OBD-app (t.ex. Car Scanner eller Torque), välj *Wi-Fi-adapter*, och skriv in datorns adress och port 35000.
- **Från datorn:** `telnet 127.0.0.1 35000`, skriv `ATZ`, `010D` (hastighet), `010C` (varvtal), `0902` (VIN).

Ändra hastighet, varvtal m.m. med reglagen – appen ser de nya värdena direkt.
All trafik syns under **OBD-logg**.

## Vad som finns i version 1.0

| Del | Status |
|---|---|
| Virtuell ELM327 via Wi-Fi (TCP 35000), Bluetooth och Bluetooth LE | Klar |
| OBD 01/03/04/09, UDS 10/22/2E/27/3E, tillverkarsignaler (21/22) | Klar |
| Android Automotive i programmet (utan omstart), teman och skal per bilmodell | Klar |
| Bilmodeller: egna, delning (.cbs), ODX/VSS/CSV, bildatabas (vPIC, Wikidata, OBDb) | Klar |
| Körningar, karta och GPS till Android | Klar |
| Testfall med rapporter | Klar |
| Riktig OBD-adapter: läsläge, vidarekoppling, skrivskydd | Klar (provad mot låtsasadapter) |
| Installationsprogram och uppdateringar (Velopack) | Klar |
## Mappar

```
src/Cyberdrome.Core   Bilens värden, bilprofiler, kodning
src/Cyberdrome.Obd    ELM327-emulatorn och TCP-servern
src/Cyberdrome.Vm     Android i virtuell maskin (WSL2 + Cuttlefish)
src/Cyberdrome.App    Programfönstret (Avalonia)
tests/                Automatiska tester (xUnit)
profiles/             Bilmodellerna (JSON) – lägg till fler här
```

## Lägga till en bilmodell

Kopiera en fil i `profiles/`, byt `id`, namn och VIN (17 tecken). Varje funktion pekar på en ECU (`ecu`), en byte och en bit i ECU:ns kodning.
Kodningen är högst 4 byte per ECU, så att en vanlig ELM327 kan skriva den i en CAN-ram.
Funktioner som stänger av säkerhetsvarningar ska inte läggas in.

## Viktigt

Alla bilmodeller, CAN-id och kodningsdata är påhittade exempel för test och motsvarar inte riktiga bilar.
Koden är egen (proprietär). Avalonia (MIT) och CommunityToolkit.Mvvm (MIT) används som paket.

## Android-skärmen i programmet (läget Utan omstart)

På **Översikt** visas Android-skärmen levande (via adb, ca 5 bilder/s). Klicka, dra (svep) och håll
direkt på skärmen. Under skärmen finns **+ Installera app** (APK), **Starta om app** (appen i förgrunden),
**Ta skärmbild** (sparas i `Skrivbordet\Projekt\skarmbilder` om mappen finns, annars `Dokument\Cyberdrome\skarmbilder`) och **Rattknappar**: föregående (88),
nästa (87) och ljud av (164) som Android-tangenthändelser.

## Bluetooth OBD-dongel ("OBDII")

Reglaget **Bluetooth (OBDII)** i kortet Virtuell OBD (Översikt) startar en Bluetooth-tjänst med
Serial Port Profile (SPP, UUID 00001101-…) som heter **OBDII**, precis som en ELM327 Bluetooth-dongel.
Varje anslutning får en egen ELM327 mot samma virtuella bil som Wi-Fi-porten, och trafiken syns i
OBD-loggen som "Bluetooth: <enhet>". Valet sparas i `%AppData%\Cyberdrome\app.json` (standard av).
Använder Windows inbyggda Bluetooth (WinRT), inga extra paket.

Så ansluter du en **Android**-telefon: para telefonen med datorn (datorns namn) i telefonens
Bluetooth-inställningar, välj sedan Bluetooth-adapter i OBD-appen och datorn i listan.
**iPhone** kan inte använda klassisk Bluetooth-OBD (SPP) – det är en begränsning i iOS. Använd i stället
reglaget **Bluetooth LE (iPhone)**: datorn blir då en BLE-OBD-dongel som Vgate iCar Pro BLE
(tjänst FFF0, FFF1 = svar/notify, FFF2 = kommandon/write). Ingen parning i iOS behövs – välj
adaptertyp Bluetooth LE i OBD-appen (t.ex. Car Scanner) och datorn i listan. Wi-Fi-porten fungerar också.

## Bilmodeller: egna och delade (.cbs)

Sidan **Bilmodeller**. De sex inbyggda bilmodellerna är **skrivskyddade**. **Ny bilmodell från kopia** öppnar en
redigerare: märke, modell, generation, årsmodell, id, VIN, felkoder vid start, PID:ar som bilen svarar på
(tomt = alla som simulatorn kan; påverkar även stödtabellerna 0100/0120/0140), startvärden, skärmförval, tema,
styrdon (nyckel, namn, CAN-id fråga/svar, kodning högst 4 byte, kodnings-DID) och dolda funktioner (styrdon,
byte, bit). Allt valideras innan det sparas. Egna modeller sparas i `%AppData%\Cyberdrome\bilar\` och visas
med **(Egen)** i listan; bara de kan redigeras och tas bort.

**Exportera bilmodell** skapar en `.cbs`-fil (zip): `bilmodell.json` (valfritt med nuvarande kodning),
plus `tema/theme.json` om ett eget tema är valt, `skal/…` om ett eget skal är valt och `testfall/*.json` för
egna testfall som hör till modellen. **Importera bilmodell** kontrollerar filen; om id:t krockar med en inbyggd
eller befintlig modell får den nytt id ("-2", "-3" …) och "(importerad)" i namnet – inbyggda skrivs aldrig över.

`.cbs` står för *Cyberdrome System*. Äldre `.cyberbil`-filer (samma innehåll) går fortfarande att importera.
Varje `.cbs` innehåller också en kopia av bilmodellen som ASAM ODX i `odx/<id>.odx-d`.

**Viktigt:** delade filer får inte innehålla riktiga tillverkares seed/key-algoritmer eller annan skyddad
information. Säkerhetsformlerna i Cyberdrome (`securityAccess`) är påhittade och ska förbli det.

## Standardformat

Det finns ingen enda standard för en hel "bilmodellfil", men Cyberdrome använder de öppna/etablerade format
som täcker delarna:

| Format | Standard | Vad det används till | Knapp (sidan Bilmodeller) |
|---|---|---|---|
| `.pdx` / `.odx-d` | ASAM MCD-2D **ODX 2.2** (ISO 22901-1) | Styrdon, CAN-id, kodning (UDS 22/2E + DID), felkoder med texter | **Exportera ODX (.pdx)**, **Importera bilmodell** |
| `*.json` | COVESA **VSS** (Vehicle Signal Specification) | Bilens värden med standardnamn, t.ex. `Vehicle.Speed` | **Exportera/Importera värden (VSS)** |
| `*.csv` | Torque / Car Scanner "custom PID" | Egna PID:ar (t.ex. mode 22) som simulatorn svarar på | **Importera/Exportera PID:ar (CSV)** |
| `*.csv` | SAE J2012-stil (`P0300;Text`) | Felkodstexter till bilmodellen | **Felkodstexter (CSV)** |

**ODX** – Cyberdrome läser den del av ODX som simulatorn kan använda: `BASE-VARIANT`/`ECU-VARIANT` blir styrdon,
`COMPARAM-REF` `CP_CanPhysReqId`/`CP_CanRespUSDTId` blir CAN-id, tjänst 0x2E (eller 0x22) med `ID`-parameter
blir kodnings-DID och längden tas från `DATA-OBJECT-PROP`/`BIT-LENGTH` (högst 4 byte), `DTC-DOP` ger felkoder
och texter. Allt som inte används listas på svenska efter importen ("Ignorerat"): flash-data, jobb,
funktionsgrupper, kommunikationsspecifikationer, 0x27-tjänster (Cyberdrome har sin egen påhittade formel) och
övriga tjänster. Exporten skriver ODX 2.2 med ett `BASE-VARIANT` per styrdon; det som ODX saknar plats för
(dolda funktioner, PID-lista, tema …) sparas i en `SDG` "Cyberdrome", så export → import är förlustfri.
Seed/key exporteras aldrig. Inga tillverkares ODX-filer följer med; testerna använder en egen påhittad exempelfil.

**VSS** – signalnamn: `Vehicle.Speed` (0D), `Vehicle.Powertrain.CombustionEngine.Speed` (0C),
`…CombustionEngine.ECT` (05), `Vehicle.Powertrain.FuelSystem.RelativeLevel` (2F), `Vehicle.OBD.ThrottlePosition` (11),
`Vehicle.OBD.IntakeTemp` (0F), `Vehicle.Exterior.AirTemperature` (46), `Vehicle.LowVoltageBattery.CurrentVoltage` (42),
`Vehicle.Powertrain.Transmission.SelectedGear`, `Vehicle.LowVoltageSystemState`, `Vehicle.OBD.DTCList` och
`Vehicle.VehicleIdentification.*`. Filen är platt JSON (`{"Vehicle.Speed": 54}`); `{"value": 54}` går också.

**Torque/Car Scanner-CSV** – kolumner `Name,ShortName,ModeAndPID,Equation,Min Value,Max Value,Units,Header`.
Simulatorn svarar på importerade PID:ar (med header = bara det styrdonet) och räknar baklänges från formeln:
`A`, `A*k`, `A/k`, `A+k`, `A-k`, `A*256+B`, `(A*256+B)/k`, `A*100/255`. Samma fil kan läsas in i appen i
telefonen. Exporten tar med kodnings-DID:arna för alla styrdon.

**DBC** (CAN-meddelanden) är inte med ännu – det blir aktuellt först om Cyberdrome simulerar rå CAN-trafik.

## Riktig OBD-adapter (sidan OBD-källa)

Cyberdrome kan ansluta till en riktig bil via en **ELM327-kompatibel** adapter (ELM327, OBDLink/STN, vLinker, Vgate m.fl.):

| Anslutning | Hur |
|---|---|
| USB eller Bluetooth via COM-port | Välj COM-port och hastighet (oftast 38400). Parkopplade klassiska Bluetooth-adaptrar får en COM-port i Windows. |
| Bluetooth (parkopplad) | Klassisk Bluetooth (SPP) direkt. Parkoppla adaptern i Windows först (kod ofta 1234 eller 0000). |
| Bluetooth LE | Tjänsterna FFF0 (FFF1/FFF2), FFE0 (FFE1), 18F0 (2AF0/2AF1, t.ex. vLinker) och OBDLink CX känns igen; annars används första tjänst med notify + skriv. |
| Wi-Fi | Anslut datorn till adapterns Wi-Fi och ange adressen (oftast 192.168.0.10:35000). |

- **Identifiera adapter** ansluter, listar BLE-tjänster och provar `ATZ`, `ATI`, `ATE0` och `0100`. Allt syns i OBD-loggen,
  och resultatet säger om adaptern är ELM327-kompatibel. Adaptrar som är låsta till en tillverkares app (som inte svarar
  på ELM327) får "Adaptern stöds inte (låst till tillverkarens app)". Deras protokoll analyseras inte.
- **Anslut** ställer adaptern i ett känt läge (`ATE0 ATL0 ATS1 ATH1 ATSP0`) och visar version (`ATI`, `STI`), protokoll
  och spänning (`ATRV`). Adapterns sparade inställningar (`ATPP`) ändras aldrig.
- **Läs riktig bil** läser stödda PID:ar (0100/0120/…), VIN (0902), kalibrerings-id (0904), felkoder (03), livevärden och
  vilka styrdon som svarar (7DF, 7E0–7E7 och vanliga UDS-adresser med `22 F190`). Bara lästjänster används.
- **Skapa virtuell bil från riktig bil** gör en egen bilmodell av avläsningen (styrdon, PID:ar, felkoder, startvärden).
  **Dölj VIN-serienummer** (på som standard) byter VIN:ets sista sex tecken mot 000001, så att filen kan delas.
- **Riktig adapter (vidarekoppla appar till bilen)**: apparna i Android och telefonen (Wi-Fi port 35000, Bluetooth, BLE)
  pratar med den riktiga bilen genom adaptern. AT-kommandon från appen hanteras av Cyberdrome; allt syns i OBD-loggen.

**Säkerhet (läsläge som standard).** Skrivfiltret stoppar allt som kan ändra något i bilen: `04` (radera felkoder),
UDS `2E`, `2F`, `31`, `34`–`37`, `3B`, `3D`, `11`, `14`, `27`, `28`, `85`, `10` med annat än `01`, och alla okända
tjänster. Appen får då ett negativt svar (`7F xx 22`) och OBD-loggen visar varför. Med **Tillåt skrivning för den här
sessionen** (nollställs när adaptern kopplas från) gäller:

1. Spänningen kontrolleras (`ATRV`). Under 12,0 V stoppas skrivningen.
2. Originalvärdet läses och sparas först i `%AppData%\Cyberdrome\backup` (`2E` → `22` samma DID, `3B` → `21`, `04`/`14` → felkoderna).
3. En dialog visar styrdon, tjänst, data, spänning och säkerhetskopian. Inget skickas utan **Skriv till bilen**.

Cyberdrome räknar aldrig ut tillverkares seed/key-algoritmer. `27` släpps bara igenom när skrivning är tillåten, och nyckeln
måste komma från appen.

## Bildatabas (sidan Bildatabas)

Bildatabasen är en lokal SQLite-fil, `%AppData%\Cyberdrome\bildatabas.db`, som programmet ansluter till. Du fyller den
från öppna källor när du själv trycker på knapparna. Inget följer med i installationen, och det som hämtats fungerar
utan internet. Programmet använder gränssnittet `IBilDatabas`, så databasen kan senare flytta till en egen Cyberdrome-server.

| Källa | Innehåll | Villkor |
|---|---|---|
| [NHTSA vPIC](https://vpic.nhtsa.dot.gov/api/) | Bilmärken, modeller per årsmodell, VIN-avkodning (mest bilar sålda i USA, årsmodell 1981–) | Öppen data, fri användning, ingen registrering |
| [Wikidata](https://www.wikidata.org) | Modellnamn per tillverkare även för europeiska bilar (t.ex. Saab 9000, Volvo 240), år när de finns | CC0: fri att använda |
| [OBDb](https://github.com/OBDb) | Tillverkarsignaler per modell (mode 21/22: styrdon, fråga, bitposition, formel, enhet) | **CC BY-SA 4.0**: källan måste anges, och ändrad data delas under samma licens |

- **Sök**: märke/modell (alla ord måste finnas) och valfritt år. Träffarna slår ihop källorna, och ★ betyder att OBDb har
  tillverkarsignaler.
- **Avkoda VIN**: vPIC. Resultatet sparas i databasen, så samma VIN fungerar sedan utan internet.
- **Hämta bilmärken / modeller (vPIC)**: per märke och årsintervall. Hämtade år hämtas inte igen.
- **Hämta fordonslista (OBDb)**: cirka 800 fordon. Signalfilen för en modell hämtas när du väljer den och sparas som ett
  separat datapaket i `%AppData%\Cyberdrome\obdb` (med `LICENS.txt`). Den blandas aldrig in i programkoden.
- **Skapa bilmodell**: gör en egen bilmodell med standard-PID:ar och OBDb:s tillverkarsignaler som simulatorn svarar på.
  Bara frågor som gäller årsmodellen tas med; frågor med utökad adressering hoppas över. Styrdon läggs till per CAN-id.
  Skärm, tema och skal kommer från närmaste inbyggda modell (samma märke, annars Golf 8). Modellens källa sparas i fältet
  `attribution`.

- **Hämta modeller (Wikidata)**: alla bilmodeller för märket (Volvo och Saab är förinställda, andra märken söks upp).
  Svaret sparas i 30 dagar. Wikidata saknar ofta årsmodeller; skriv då året i fältet **År** innan du trycker
  **Skapa bilmodell**.
- Om OBDb:s fil för modellen saknas eller är tom används märkets gemensamma signaler (t.ex. arkivet `Volvo`). Finns
  inga signaler alls (i dag t.ex. Saab 9-3 och 9-5, vars OBDb-arkiv ännu är tomma) får bilmodellen standard-PID:ar.
- **OBD-II gäller USA från 1996, EU-bensinbilar från 2001 och EU-dieslar från 2004.** Äldre årsmodeller får en varning,
  och modeller före 1996 sparas som 1996.
- **Skapa standard-OBD-bil** (när modellen inte finns i någon källa): skriv märke, modell och år. Bilmodellen får
  standard-OBD-II (PID:ar, VIN, felkoder). Tillverkarsignaler läggs till senare på sidan **Bilmodeller** med
  **Importera PID:ar (CSV)** (Torque/Car Scanner) eller **Importera signaler (OBDb-JSON)** (format `signalsets/v3`).
  En fråga till samma styrdon ersätts, och andra frågor behålls.
- Bilmodeller av märken utan inbyggd modell får ett påhittat VIN med rätt tillverkarkod och årsmodell
  (t.ex. Saab 2008 → `YS3ZZZ00080000001`).
- Källor som **inte** används: tillverkarnas verkstadsprogram (t.ex. Volvo VIDA, Saab Tech2/WIS), databaser som härletts
  ur dem, och forum. Deras innehåll är upphovsrättsskyddat.

Nätverk: vPIC och GitHub frågas med paus mellan anropen, och vid fel 429/5xx görs nya försök med väntetid. Om GitHubs gräns
för antal frågor nås står det när man kan försöka igen; sparad data fungerar under tiden.

**Kontroll inifrån Android:** Cyberdrome Launcher har ett utvecklarverktyg som skickar OBD-kommandon inifrån Android
och skriver svaren i logcat:

```
adb shell "am broadcast -n se.cyberdrome.launcher/.ObdProbeReceiver -a se.cyberdrome.launcher.PROBE --es cmds 'ATZ;ATE0;ATSH700;221002'"
adb logcat -d -s CyberdromeProbe:I
```

## Testfall (godkänt/underkänt)

Sidan **Testfall** kör ett testfall medan en app är ansluten till den virtuella OBD:n (i Android, eller en
telefon via Wi-Fi/Bluetooth). Cyberdrome utför stegen (ändrar värden, tänder/släcker felkoder) och kontrollerar
sedan OBD-trafiken. Resultatet visas per kontroll med utdrag ur loggen och sparas som **HTML + JSON** i
`%AppData%\Cyberdrome\testresultat\`. Inbyggda testfall: **Grundläggande PID:ar**, **Felkoder: läs och radera**,
**Kodning via UDS 22**. Egna testfall importeras (JSON) och sparas i `%AppData%\Cyberdrome\testfall\`;
"Exportera som mall" ger en fil att utgå från.

```json
{ "id": "mitt-test", "name": "Mitt test", "description": "…", "profile": "vw-golf8", "drive": "Stadskörning 5 min",
  "values": { "speed": 50, "rpm": 2000, "coolant": 88, "fuel": 60, "ignition": true }, "durationSeconds": 30,
  "steps": [ { "at": 5, "action": "addDtc", "dtc": "P0300", "text": "Läs felkoder i appen" },
             { "at": 10, "action": "set", "values": { "speed": 90 } } ],
  "checks": [ { "type": "requestRate", "name": "Fart ofta", "command": "010D", "maxGapSeconds": 2 },
              { "type": "responseContains", "name": "P0300 syns", "command": "03", "text": "0300", "afterStep": 0 },
              { "type": "requestSeen", "name": "Raderar", "command": "04", "optional": true },
              { "type": "noErrors", "name": "Inga felsvar" } ] }
```
Steg: `set`, `addDtc`, `removeDtc`, `clearDtcs`, `note`. Kontroller: `requestRate` (minst en fråga per N s; `010D`
matchar även flera PID:ar i samma fråga), `requestSeen`, `responseContains` (mellanslag/rubriker ignoreras),
`noErrors` (inga `?` eller `NO DATA` på OBD-frågor; AT/ST-sonderingar räknas inte). `optional` ger "Ej utförd"
i stället för "Underkänd". **Snabbval för vanliga felkoder** (P0300, P0420 m.fl.) finns på Översikt.

## Utseende per bilmodell: tema i Android och skal runt skärmen

**Tema** = startskärmen inne i Android, vår egen app **Cyberdrome Launcher** (`android\CyberdromeLauncher`).
Varje bilmodell har ett eget tema som efterliknar märkets system – layout, färger, rutor och stil –
men **allt är ritat av oss: inga logotyper, märkesfonter, ikoner eller tillverkargrafik** (bara Androids egna
Roboto-varianter). Fordonsdata på startskärmen kommer bara från OBD.

| Bil | Layout | Skärm |
|---|---|---|
| Golf 8 | rutnät med widgetrutor, blå accent | 10" 1560×700 |
| Octavia 4 | rutnät, grön accent, rundare rutor | 10" 1280×800 |
| 3-serie G20 | smal meny till vänster, breda rutor i rad | 12,3" 1920×720 |
| XC60 | stående, fyra staplade rutor, klimatrad | 9" 768×1024 stående |
| RAV4 | delad startskärm, blå/vit knapprad | 8" 800×480 |
| Focus Mk4 | tre stora fält + flikrad med en färg per flik | 8" 800×480 |

När du byter bilmodell byter Cyberdrome automatiskt **tema, skärmformat och skal**. Valen kan ändras per bilmodell
(Översikt eller Inställningar → Utseende): bilens eget (standard), ett annat inbyggt, ett eget, eller av
(Androids vanliga startskärm). Launchern byggs och installeras automatiskt vid behov och sätts som startskärm
(`cmd package set-home-activity`). Temat skickas med `am broadcast -n se.cyberdrome.launcher/.ThemeReceiver`.

**Eget tema** (Ladda upp eget tema … – zip eller theme.json), sparas i `%AppData%\Cyberdrome\teman\`:
```json
{ "id": "mitt-tema", "name": "Mitt tema", "layout": "tiles | leftmenu | portrait | split | tabs",
  "colors": { "background": "#0B0F17", "surface": "#1A2333", "accent": "#3BA7FF", "text": "#FFFFFF", "muted": "#8FA3BF" },
  "cornerRadius": 14, "font": "sans | light | condensed | medium", "wallpaper": "gradient | lines | dots | plain",
  "tabColors": ["#F28C28","#E5484D","#F5C518","#3DBE6B","#9B6CF2","#8E8E93"], "screenPreset": "10in-1280x800" }
```

**Skal** = ramen/instrumentbrädan runt skärmen i Cyberdrome, med knappar och vred som skickar Android-tangenter
(hem 3, tillbaka 4, volym 24/25, ljud av 164, spela/paus 85, nästa 87, föregående 88). **Eget skal**
(Ladda upp eget skal …), sparas i `%AppData%\Cyberdrome\skal\<id>\`: antingen en **PNG där skärmytan är genomskinlig**
(skärmytan hittas automatiskt) eller en zip/mapp med bild + `skin.json`:
```json
{ "id": "min-ram", "name": "Min ram", "width": 1600, "height": 900, "image": "ram.png",
  "screen": { "x": 300, "y": 120, "w": 1000, "h": 625 }, "screenPreset": "10in-1280x800", "orientation": "Landscape",
  "buttons": [ { "name": "Hem", "label": "⌂", "rect": { "x": 700, "y": 790, "w": 200, "h": 60 }, "keyCode": 3 } ] }
```
Skärmytan och alla knappar måste ligga inne i bilden. Använd bara bilder du själv har rätt till.

## Skärmstorlek (Inställningar → Skärm, och snabbval på Översikt)

Förval: **10,1" 1280×800 160 dpi (standard)**, 10,1" 1024×600, 7" 1024×600 170 dpi, 7" 800×480 133 dpi,
9" 1280×720, 12,3" 1920×720, 10,4" Tesla-stil 1920×1200 / 1920×1080 213 dpi, samt **Anpassad**.
Varje förval kan väljas **liggande eller stående** (bredd och höjd byts). Bytet sker direkt i emulatorn med
`wm size` / `wm density` (rotationen låses), och sparas i AVD:ns `config.ini` till nästa kallstart.
Android-skärmen i Cyberdrome behåller rätt proportioner (svarta kanter) och klick räknas om till rätt pixel.
OBS: Android Automotives egen systemmeny är byggd för liggande skärm och ser trång ut i stående läge.

## Körningar (spela in / spela upp)

Sidan **Körningar**: spela in bilens värden (var 100:e ms) medan du drar i reglagen, eller spela upp en
sparad körning i 1×/2×/5×, med slinga, paus och tidslinje. Under uppspelning är reglagen låsta och följer
körningen. Körningarna sparas som JSON i `%AppData%\Cyberdrome\korningar\` (exportera/importera finns).
Tre inbyggda exempel (egen genererad data, samma varje gång): **Stadskörning 5 min** (centrala Stockholm),
**Motorväg 10 min** (E4 norr om Stockholm), **Kallstart** (parkerad, kylvätska 5 → 90 °C).

## Karta och GPS

Sidan **Karta** visar bilen på en karta (OpenStreetMap-rutor, egen kartkontroll). Klicka för att flytta
bilen, eller klicka ut en rutt och tryck **Kör rutten** – bilen följer rutten med farten från reglaget eller
körningen. Körningar sparar latitud/longitud per punkt. Varje sekund skickas positionen till Android-emulatorn
med `adb emu geo fix <lon> <lat> <höjd> <satelliter> <fart i knop>`, så Androids GPS-fart är samma som OBD-farten
(kursen räknar Android själv). Platstjänster i emulatorn slås på automatiskt.

**Kartrutor:** standard är OpenStreetMaps egna rutor, som enligt deras regler bara får användas måttligt.
Adressen går att ändra på sidan Karta (`MapTileUrl` i `%AppData%\Cyberdrome\app.json`). **Den sålda produkten
behöver en kommersiell kartleverantör.** Rutorna cachas i `%LocalAppData%\Cyberdrome\tiles`.
Kartdata © OpenStreetMap-bidragsgivare (ODbL).

**Kartapp i Android:** knappen **Installera kartapp (Organic Maps)** laddar ner Organic Maps (Apache-2.0) från
F-Droid när du trycker – den följer inte med Cyberdrome. Organic Maps laddar själv ner kartan för området.

## Exempelapp: Cyberdrome Radio (android\CyberdromeRadio)

Vår egen Android Automotive-app som mall för radio-/mediamjukvara. Egen kod, inga tredjepartsbibliotek.
- Påhittade stationer (Neon FM, Radio Puls, Stadsradion, Kustradio, Retro 80). Ljudet är egna toner som
  genereras i appen (AudioTrack) – ingen ström och inget upphovsrättsskyddat ljud.
- `MediaSession` + `MediaBrowserService`: rattknapparna (nästa/föregående) byter station, play/paus fungerar.
- Live OBD via ELM327 över Wi-Fi: standard `10.0.2.2:35000` (går att ändra i appen), läser hastighet (010D)
  och varvtal (010C) ca 2 ggr/s och återansluter av sig själv.
- **Volym efter fart:** +1 steg per 30 km/h, högst +5, kan slås av.

Bygg utan Gradle: `powershell -ExecutionPolicy Bypass -File android\CyberdromeRadio\build.ps1`
(eller VS Code-uppgiften *build radio*). Resultat: `android\CyberdromeRadio\bin\CyberdromeRadio.apk`.
Knappen **Installera Cyberdrome Radio** i Cyberdrome bygger vid behov och installerar appen i emulatorn.
Kräver `platforms;android-34` och `build-tools;34.0.0` i SDK:n. Signeras med en lokal debug-nyckel
(`%LocalAppData%\Cyberdrome\debug.keystore`).

## OBD-testapp: AndrOBD

Knappen **Installera testapp (AndrOBD)** (Översikt och sidan Android) laddar ner den senaste
AndrOBD från F-Droid (`com.fr3ts0n.ecu.gui.androbd`), kontrollerar att filen är en giltig APK,
sparar den i `%LocalAppData%\Cyberdrome\apps\` och installerar den i emulatorn (`adb install -r -g`).
Anslut i AndrOBD med anslutningstyp **Nätverk**, IP **10.0.2.2**, port **35000**.

**Licens:** AndrOBD är öppen källkod under GPLv3. Den laddas ner från F-Droid när användaren trycker
på knappen och **följer inte med Cyberdrome**, så Cyberdromes egen licens påverkas inte.

## Säkerhetsnyckel (UDS 0x27) i de simulerade styrdonen

Kodning med `2E` kräver utökad session (`10 03`) **och** upplåsning: `27 01` ger ett seed (4 byte),
`27 02 <nyckel>` låser upp (`67 02`). Fel nyckel ger `7F 27 35`; efter 3 fel `7F 27 36` och 10 sekunders
spärr (`7F 27 37`). Låst skrivning ger `7F 2E 33`. `10 01` eller `ATZ` låser igen. OBD-loggen visar
den förväntade nyckeln efter varje seed, så att du kan testa din egen app.

Formeln står i bilprofilen (`"securityAccess": { "xorMask", "rotateLeft", "add" }`, även per styrdon):
`nyckel = rotl(seed XOR xorMask, rotateLeft) + add`. **Den är påhittad** för simuleringen och motsvarar
ingen riktig biltillverkares algoritm. Reglaget **Kräv säkerhetsnyckel (UDS 27)** på sidan Dolda
funktioner stänger av kravet för tester.

## Android-läge (Inställningar)

Under **Inställningar** väljer du hur Android körs. Valet sparas i `%AppData%\Cyberdrome\android.json` (fältet `Mode`).

- **Utan omstart (Googles emulator, för utveckling)** – standard. Använder Android-emulatorn i
  `C:\CyberdromeSDK` (fältet `SdkRoot`) med Windows Hypervisor Platform. Ingen omstart behövs.
  Installera Android laddar ner Android Automotive-avbilden (`SystemImage`) med förloppsstapel,
  skapar den virtuella bilen och startar den direkt. Appar inne i emulatorn når den virtuella
  OBD-porten på **10.0.2.2:35000**.
  **OBS:** Googles emulatoravbilder har licensvillkor som gör att de inte får följa med i den sålda produkten.
- **Snabbt – WSL2 + Cuttlefish (riktig AOSP)** – se nedan. Kräver en omstart första gången om
  Virtual Machine Platform inte är påslaget; programmet frågar först (Fortsätt/Avbryt).

## Android i läget Snabbt (WSL2 + Cuttlefish)

Sidan **Android** i programmet gör allt i ordning:
1. Slår på WSL2 om det saknas (programmet förklarar först, sedan frågar Windows om administratörslov, sedan omstart).
2. Skapar en egen Linux-miljö, `Cyberdrome-Android`, som inte rör annan Linux på datorn.
3. Installerar Cuttlefish och laddar ner Android Automotive (flera GB).
4. Startar Android. Skärmen visas i webbläsaren på https://localhost:8443.

Inställningar (t.ex. vilket Android-bygge som hämtas, antal CPU och minne) finns i
`%AppData%\Cyberdrome\android.json`.

Under utveckling används Googles färdiga Cuttlefish-bygge. Det får inte följa med säljversionen;
där byts det mot vår egen AOSP-avbild.

Om Windows **Smart App Control** är på blockeras nybyggda filer. Stäng av den på utvecklingsdatorn.
