# Privacy / Integritet – Cyberdrome Cockpit

## English

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it.

Cyberdrome Cockpit has no telemetry, no analytics, no account and no advertising. Your car models, drives, test
results and settings stay on your computer (`%AppData%\Cyberdrome` and `%LocalAppData%\Cyberdrome`).

The program only connects to other systems for features you use. Each one can be avoided:

| When | Where it connects | What is sent | How to turn it off |
| --- | --- | --- | --- |
| Checking for updates (at start, at most once a day, and when you press "Check for updates") | GitHub (`api.github.com`, `github.com`) | A normal web request for the list of releases. No personal data, no ID. GitHub sees your IP address. | Settings → Updates → Advanced → turn off "Check automatically at start". |
| Installing Android (Settings → Android mode, page Android) | Google (`dl.google.com`) for the emulator, or the Android Build API / Ubuntu archives for WSL2 + Cuttlefish | Download requests only. | Do not press "Install Android". |
| Map page | The map tile server you choose (default `tile.openstreetmap.org`) | The map area you look at. | Do not open the Map page, or set your own tile server. |
| Car database page | NHTSA vPIC, Wikidata, OBDb on GitHub | Your search words, a VIN you choose to decode. | Do not press the fetch / decode buttons. |
| Test apps (AndrOBD, Organic Maps) | F-Droid (`f-droid.org`) | Download requests only. | Do not press the install buttons. |
| Donation button | Opens PayPal in your browser | Nothing is sent by the program. | Do not press it. |

The virtual OBD port and the Bluetooth dongles only listen on your own computer and local network; they are turned on
and off on the Overview page. A real OBD adapter is only used when you connect it on the OBD source page.

Third-party services have their own privacy policies: [GitHub](https://docs.github.com/site-policy/privacy-policies),
[Google](https://policies.google.com/privacy), [OpenStreetMap](https://osmfoundation.org/wiki/Privacy_Policy),
[NHTSA](https://www.nhtsa.gov/about-nhtsa/privacy-policy), [Wikimedia](https://foundation.wikimedia.org/wiki/Policy:Privacy_policy),
[F-Droid](https://f-droid.org/about/), [PayPal](https://www.paypal.com/privacy).

## Svenska

Programmet skickar ingen information till andra system i nätverket om inte användaren, eller den som installerar
eller kör programmet, uttryckligen ber om det.

Cyberdrome Cockpit har ingen telemetri, ingen statistik, inget konto och ingen reklam. Dina bilmodeller, körningar,
testresultat och inställningar stannar på datorn (`%AppData%\Cyberdrome` och `%LocalAppData%\Cyberdrome`).

Programmet ansluter bara till andra system för funktioner du använder:

| När | Vart | Vad som skickas | Så stänger du av det |
| --- | --- | --- | --- |
| Uppdateringskontroll (vid start, högst en gång per dygn, och när du trycker "Sök efter uppdateringar") | GitHub | En vanlig förfrågan efter listan med releaser. Inga personuppgifter, inget id. GitHub ser din IP-adress. | Inställningar → Uppdateringar → Avancerat → stäng av "Sök automatiskt vid start". |
| Installera Android | Google, eller Android Build API / Ubuntu för WSL2 + Cuttlefish | Bara nedladdningar. | Tryck inte "Installera Android". |
| Sidan Karta | Kartservern du väljer (standard OpenStreetMap) | Kartområdet du tittar på. | Öppna inte Karta, eller välj egen kartserver. |
| Sidan Bildatabas | NHTSA vPIC, Wikidata, OBDb på GitHub | Dina sökord, ett VIN du väljer att avkoda. | Tryck inte på hämta-/avkoda-knapparna. |
| Testappar (AndrOBD, Organic Maps) | F-Droid | Bara nedladdningar. | Tryck inte på installera-knapparna. |
| Donationsknappen | Öppnar PayPal i webbläsaren | Programmet skickar inget. | Tryck inte på den. |

Den virtuella OBD-porten och Bluetooth-donglarna lyssnar bara på din dator och ditt lokala nätverk. En riktig
OBD-adapter används bara när du ansluter den på sidan OBD-källa. Tredjepartstjänsterna har egna integritetspolicyer
(länkar ovan).
