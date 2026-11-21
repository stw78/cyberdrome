# Changelog / Ändringslogg

Each version has an English and a Swedish part. The release job on GitHub uses the section for the tagged version as
release notes (`tools/changelog-section.ps1`). / Varje version har en engelsk och en svensk del. Releasejobbet på GitHub
använder avsnittet för den taggade versionen som releaseanteckningar.

## 1.2.0

**English**

- **Live Android screen:** the emulator modes stream the screen continuously (gRPC) at up to ~60 frames per second
  instead of about 3, drawn straight into the program without PNG decoding. Tap, drag and swipe go to Android in real
  time (~1 ms).
- **WSL2 + Cuttlefish is now usable:** Android draws on the graphics card (gfxstream via WSL2 /dev/dxg and Mesa D3D12,
  software as a fallback), and the program shows Cuttlefish's WebRTC video stream (~50–60 frames per second) instead
  of screenshots. The screen size follows Settings, and the car theme (Cyberdrome Launcher) is used here too.
- **Fast start:** after the first start the emulator resumes from a saved state in a few seconds (quickboot).
- **No black borders:** the picture always fills the skin's screen, also after changing the screen size while running.
- **Back button:** ← Back and ⌂ Home in the toolbar, Back on every skin and in fullscreen; right-click or the mouse
  back button = Back, middle-click = Home, Backspace = Back.
- The car home screen gets the right OBD address in every mode, and the program notices when Android is started or
  stopped outside it.

**Svenska**

- **Levande Android-skärm:** emulatorlägena strömmar skärmen kontinuerligt (gRPC) med upp till cirka 60 bilder per
  sekund i stället för cirka 3, direkt till programmet utan PNG-avkodning. Tryck, dra och svep går i realtid (~1 ms).
- **WSL2 + Cuttlefish är nu användbart:** Android ritar på grafikkortet (gfxstream via WSL2:s /dev/dxg och Mesa D3D12,
  mjukvara som reserv), och programmet visar Cuttlefishs WebRTC-videoström (cirka 50–60 bilder per sekund) i stället
  för skärmdumpar. Skärmstorleken följer Inställningar, och bilens tema (Cyberdrome Launcher) används även här.
- **Snabbstart:** efter första starten fortsätter emulatorn från ett sparat läge på några sekunder (quickboot).
- **Inga svarta kanter:** bilden fyller alltid skalets skärm, även när skärmstorleken ändrats under körning.
- **Tillbaka-knapp:** ← Tillbaka och ⌂ Hem i verktygsraden, Tillbaka på alla skal och i helskärm; högerklick eller
  musens bakåtknapp = Tillbaka, mittenklick = Hem, Backspace = Tillbaka.
- Bilens startskärm får rätt OBD-adress i alla lägen, och programmet märker när Android startas eller stängs utanför det.

## 1.1.2

**English**

- Much faster Android: the emulator now uses the computer's graphics card (`-gpu host`) instead of software rendering,
  with software as an automatic fallback if the card cannot be used.
- Android gets half of the computer's threads (2–8 cores) instead of always 4.
- The Android screen in the program updates about 15 times per second instead of about 3 (screenshots taken by the
  emulator itself, ~25 ms instead of ~230 ms), and taps respond in ~35 ms instead of ~100 ms.
- Shorter animations in Android (0.5×) so menus feel quicker, and no emulator sound.
- WSL2 + Cuttlefish: the screen is fetched with Windows' own adb instead of starting wsl.exe for every frame.
- The graphics card and cores apply the next time Android starts: turn Android off and on with the switch.

**Svenska**

- Mycket snabbare Android: emulatorn använder nu datorns grafikkort (`-gpu host`) i stället för mjukvarugrafik, med
  mjukvara som automatisk reserv om grafikkortet inte går att använda.
- Android får hälften av datorns trådar (2–8 kärnor) i stället för alltid 4.
- Android-skärmen i programmet uppdateras cirka 15 gånger per sekund i stället för cirka 3 (bilden tas av emulatorn
  själv, ~25 ms i stället för ~230 ms), och tryck svarar på ~35 ms i stället för ~100 ms.
- Kortare animationer i Android (0,5×) så att menyer känns kvickare, och inget emulatorljud.
- WSL2 + Cuttlefish: skärmen hämtas med Windows egen adb i stället för att starta wsl.exe för varje bild.
- Grafikkortet och kärnorna gäller från nästa gång Android startar: slå Android av och på med brytaren.

## 1.1.1

**English**

- WSL2 + Cuttlefish: Android now actually starts (about 30 seconds). Cuttlefish's crosvm sandbox does not work in WSL2,
  so Android is started without it, half-started instances are cleaned up first, and a start that takes too long is
  stopped with the reason from the log and a **Show log** link – it never waits forever.
- The Android screen is shown inside the program in the WSL2 mode too, and apps in Android reach the virtual OBD port.
- New **Android on/off switch** (Off, Starting…, On, Shutting down…) first on Overview, in the top bar and on the
  Android page. It replaces the Start and Stop buttons.
- The program icon now shows in the Windows Start menu and on shortcuts (the icon file now has bitmap sizes).
- No build warnings; a text mix-up between "Passed"/"PASSED" in test results is fixed.

**Svenska**

- WSL2 + Cuttlefish: Android startar nu på riktigt (cirka 30 sekunder). Cuttlefish-sandlådan för crosvm fungerar inte
  i WSL2, så Android startas utan den, halvstartade instanser städas bort först, och en start som tar för lång tid
  avbryts med orsaken ur loggen och länken **Visa logg** – den väntar aldrig för evigt.
- Android-skärmen visas inne i programmet även i WSL2-läget, och appar i Android når den virtuella OBD-porten.
- Ny **på/av-brytare för Android** (Av, Startar…, På, Stänger…) först på Översikt, i toppfältet och på sidan Android.
  Den ersätter knapparna Starta och Stoppa.
- Programikonen syns nu i Windows Start-meny och på genvägar (ikonfilen har nu bitmappsstorlekar).
- Inga byggvarningar; en sammanblandning av texterna "Godkänd"/"GODKÄND" i testresultaten är rättad.

## 1.1.0

**English**

- New name: **Cyberdrome Cockpit** (was Cyberdrome Simulator). Existing installations update in place; your data stays.
- New, simpler design on every page: grouped side menu with icons, page titles, soft cards, segmented controls,
  iOS-style switches and one consistent icon set (Lucide).
- Overview: the car skin always scales to fit, the toolbar wraps instead of being cut off, and a new **Fullscreen**
  button (F11, Esc to leave) shows only the car screen with its skin. Taps still land on the right Android pixel.
- Settings fits on one screen: the three Android modes are tiles with status, and the page scrolls on small windows.
- WSL2 + Cuttlefish: "Install Android" no longer does nothing. The check now finds the new Cuttlefish packages (`cvd`),
  the Android build target was updated, and every stop shows the reason and the next step (WSL, Virtual Machine
  Platform, restart, KVM/nested virtualisation).
- Updates now come from **GitHub Releases**: checked at start at most once a day and with "Check for updates", with
  release notes and a progress bar. Pre-releases and a local test folder are under Advanced.
- This build is **not signed** with a trusted certificate yet, so Windows SmartScreen warns. Choose **More info → Run
  anyway**. Trusted signing via SignPath Foundation is planned.

**Svenska**

- Nytt namn: **Cyberdrome Cockpit** (tidigare Cyberdrome Simulator). Befintliga installationer uppdateras på plats;
  dina data finns kvar.
- Ny, enklare design på alla sidor: grupperad sidomeny med ikoner, sidrubriker, mjuka kort, segmentknappar,
  iOS-liknande brytare och en enda ikonfamilj (Lucide).
- Översikt: bilens skal skalas alltid så att det ryms, verktygsraden radbryts i stället för att skäras av, och en ny
  **Fullskärm**-knapp (F11, Esc för att lämna) visar bara bilskärmen med skalet. Tryck hamnar fortfarande rätt i Android.
- Inställningar ryms på en skärm: de tre Android-lägena är rutor med status, och sidan går att rulla i små fönster.
- WSL2 + Cuttlefish: "Installera Android" gör inte längre ingenting. Kontrollen hittar nu de nya Cuttlefish-paketen
  (`cvd`), Android-byggmålet är uppdaterat, och varje stopp visar orsaken och nästa steg (WSL, Plattform för virtuella
  datorer, omstart, KVM/nästlad virtualisering).
- Uppdateringar hämtas nu från **GitHub Releases**: kontrolleras vid start högst en gång per dygn och med "Sök efter
  uppdateringar", med nyheter och förloppsstapel. Förhandsversioner och en lokal testmapp finns under Avancerat.
- Den här versionen är **inte signerad** med ett betrott certifikat än, så Windows SmartScreen varnar. Välj **Mer
  information → Kör ändå**. Betrodd signering via SignPath Foundation är planerad.

## 1.0.1

**English** – Bug fixes for the installer and updates from a local folder.

**Svenska** – Buggrättningar för installationen och uppdateringar från en lokal mapp.

## 1.0.0

**English** – First public release: Android head unit simulator with a virtual OBD-II port, car models, drives, map,
test cases, real OBD adapter (read-only by default), English and Swedish.

**Svenska** – Första publika versionen: simulator för Android-bilstereo med virtuell OBD-II-port, bilmodeller,
körningar, karta, testfall, riktig OBD-adapter (läsläge som standard), svenska och engelska.
