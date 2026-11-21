# Bidra till Cyberdrome / Contributing

Tack för att du vill hjälpa till! / Thank you for helping!

## Svenska

1. **Fråga först om större ändringar.** Öppna ett ärende (issue) och beskriv vad du vill göra.
2. **Bygg och testa.** `dotnet build` och `dotnet test` måste gå igenom. Lägg till tester för ny logik
   (`tests/Cyberdrome.Tests`, xUnit).
3. **Licens.** Bidrag tas emot under **GPL-3.0-or-later**. Nya källfiler börjar med:
   ```
   // SPDX-License-Identifier: GPL-3.0-or-later
   // Copyright (C) 2026 <ditt namn>
   ```
4. **Detta får inte checkas in:**
   - tillverkares data eller filer (ODX-filer från verkstadsprogram, VIDA, Tech2/WIS, ODIS m.fl.), logotyper,
     typsnitt eller seed/key-algoritmer,
   - Google-avbildningar eller andra tredjepartsbinärer,
   - lösenord, certifikat eller nycklar (`signing.json`, `*.pfx`),
   - personuppgifter (riktiga VIN, sökvägar med användarnamn).
5. **Säkerhet för riktiga bilar.** Skrivfiltret (läsläge som standard, säkerhetskopia, spänningskontroll och
   bekräftelse per skrivning) får inte försvagas.
6. **Bilmodeller.** Nya inbyggda bilmodeller ska ha påhittade värden. Signaler från OBDb hämtas vid körning och läggs
   inte i repot (CC BY-SA 4.0).
7. **Språk.** Alla texter som användaren ser finns i `src/Cyberdrome.Localization/Strings.resx` (engelska, reserv) och
   `Strings.sv.resx` (svenska). I koden: `L.T("Nyckel")` eller `L.F("Nyckel", värde)`; i XAML: `{l:T Nyckel}`.
   Skriv aldrig text direkt i koden – testerna (`LocalizationTests`) stoppar svenska texter och hårdkodade texter i
   gränssnittet. Android-apparna har sina texter i `android/*/res/values/strings.xml` och `values-sv/strings.xml`.
   **Lägga till ett språk** (t.ex. tyska):
   1. Kopiera `Strings.resx` till `Strings.de.resx` och översätt värdena (behåll nycklar och `{0}`-platser).
   2. Lägg till filen i `Cyberdrome.Localization.csproj` (som `Strings.sv.resx`) och språket i `L.Languages`
      (`["de"] = "Deutsch"`).
   3. För Android-apparna: kopiera `res/values-sv` till `res/values-de` och översätt.
   4. Kör `dotnet test` – testet kontrollerar att alla nycklar och platser finns i det nya språket.
8. **PowerShell-skript** sparas som UTF-8 med BOM (Windows PowerShell 5.1 läser annars å/ä/ö fel).

## English

1. **Ask first for larger changes.** Open an issue and describe what you want to do.
2. **Build and test.** `dotnet build` and `dotnet test` must pass. Add tests for new logic
   (`tests/Cyberdrome.Tests`, xUnit).
3. **Licence.** Contributions are accepted under **GPL-3.0-or-later**. New source files start with the SPDX header
   shown above.
4. **Never commit:**
   - manufacturer data or files (ODX from dealer tools, VIDA, Tech2/WIS, ODIS etc.), logos, fonts or seed/key
     algorithms,
   - Google system images or other third-party binaries,
   - passwords, certificates or keys,
   - personal data.
5. **Safety for real cars.** The write filter must not be weakened. This covers read-only by default, backup before
   write, voltage check and confirmation of every write.
6. **Car models.** Built-in car models use made-up values. OBDb signals are downloaded at runtime and are not stored
   in the repo (CC BY-SA 4.0).
7. **Languages.** All user-visible text is in `src/Cyberdrome.Localization/Strings.resx` (English, fallback) and
   `Strings.sv.resx` (Swedish). In code: `L.T("Key")` or `L.F("Key", value)`; in XAML: `{l:T Key}`. Never
   hard-code text; the tests (`LocalizationTests`) fail on Swedish literals and hard-coded UI text. The Android apps
   keep their texts in `android/*/res/values/strings.xml` and `values-sv/strings.xml`.
   **Adding a language** (e.g. German):
   1. Copy `Strings.resx` to `Strings.de.resx` and translate the values (keep the keys and the `{0}` placeholders).
   2. Add the file to `Cyberdrome.Localization.csproj` (like `Strings.sv.resx`) and the language to `L.Languages`
      (`["de"] = "Deutsch"`).
   3. For the Android apps: copy `res/values-sv` to `res/values-de` and translate.
   4. Run `dotnet test` – the test checks that every key and placeholder exists in the new language.
8. **PowerShell scripts** are saved as UTF-8 with BOM (Windows PowerShell 5.1 otherwise misreads non-ASCII text).
