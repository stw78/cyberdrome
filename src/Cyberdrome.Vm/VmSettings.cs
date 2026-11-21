// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cyberdrome.Vm;

/// <summary>Hur Android körs.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AndroidMode>))]
public enum AndroidMode
{
    /// <summary>WSL2 + Cuttlefish. Kräver en omstart första gången om Virtual Machine Platform inte är påslaget.</summary>
    Fast,

    /// <summary>Googles Android-emulator direkt i Windows (Windows Hypervisor Platform). Ingen omstart. Bara för utveckling.</summary>
    NoRestart,

    /// <summary>Som NoRestart, men med Googles appar (Play Store, Google Maps). Bara för utveckling.</summary>
    GoogleApps,
}

/// <summary>
/// Inställningar för Android-maskinen. Sparas i %AppData%\Cyberdrome\android.json
/// så att t.ex. vilken Android-version som laddas ner kan ändras utan ny programversion.
/// </summary>
public sealed class VmSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Hur Android körs. Standard: utan omstart av datorn (Googles emulator).</summary>
    public AndroidMode Mode { get; set; } = AndroidMode.NoRestart;

    // ================= Läge "Snabbt": WSL2 + Cuttlefish =================

    /// <summary>Namnet på vår egen WSL-distribution (påverkar inte användarens andra Linux).</summary>
    public string DistroName { get; set; } = "Cyberdrome-Android";

    /// <summary>Linux som distributionen bygger på.</summary>
    public string BaseDistro { get; set; } = "Ubuntu-24.04";

    /// <summary>
    /// Vilket Android-bygge som hämtas (gren/mål). Under utveckling används ett färdigt
    /// Cuttlefish-bygge. I säljversionen byts detta mot vår egen AOSP-avbild.
    /// </summary>
    public string AndroidBuild { get; set; } = "aosp-android-latest-release/aosp_cf_x86_64_auto-userdebug";

    /// <summary>Byggmål som Android Build API inte längre känner till (400 Bad Request) och som byts ut vid inläsning.</summary>
    public static readonly string[] RetiredBuilds = ["aosp-android-latest-release/aosp_cf_x86_64_only_auto-userdebug"];

    /// <summary>
    /// Ungefär hur stor nedladdningen är (MB), för förloppsstapeln. Uppskattning första gången;
    /// efter en lyckad nedladdning sparas den verkliga storleken här.
    /// </summary>
    public long ExpectedDownloadMb { get; set; } = 5000;

    /// <summary>Kärnor till Android: hälften av datorns trådar, 2–8 (förr alltid 4).</summary>
    public int Cpus { get; set; } = DefaultCpus();

    public static int DefaultCpus() => DefaultCpusFor(Environment.ProcessorCount);
    public static int DefaultCpusFor(int threads) => Math.Clamp(threads / 2, 2, 8);

    /// <summary>
    /// Grafik i emulatorn: "host" = datorns grafikkort (snabbt), "swiftshader_indirect" = mjukvara (långsamt, fungerar alltid).
    /// Startar inte emulatorn med grafikkortet provas mjukvara automatiskt.
    /// </summary>
    public string EmulatorGpu { get; set; } = "host";

    /// <summary>Kortare animationer i Android (0,5×) så att gränssnittet känns snabbare. Standard: på.</summary>
    public bool FastAnimations { get; set; } = true;

    /// <summary>Vilken version av prestandainställningarna som gäller (för att uppgradera gamla inställningsfiler en gång).</summary>
    public int PerformanceVersion { get; set; }
    public const int CurrentPerformanceVersion = 1;

    /// <summary>Satt när en gammal fil uppgraderades vid inläsning (sparas då direkt).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool Migrated { get; private set; }
    public int MemoryMb { get; set; } = 8192;

    /// <summary>Adress till Android-skärmen (Cuttlefish WebRTC).</summary>
    public string ScreenUrl { get; set; } = "https://localhost:8443";

    public int AdbPort { get; set; } = 6520;

    // ================= Läge "Utan omstart": Android-emulatorn =================

    /// <summary>Var Android SDK (emulator, sdkmanager, adb, Java) finns.</summary>
    public string SdkRoot { get; set; } = @"C:\CyberdromeSDK";

    /// <summary>Vilken Android Automotive-avbild som laddas ner (sdkmanager-paket).</summary>
    public string SystemImage { get; set; } = "system-images;android-34-ext9;android-automotive;x86_64";

    /// <summary>Namnet på den virtuella bilen (AVD) som programmet skapar.</summary>
    public string AvdName { get; set; } = "Cyberdrome_Automotive";

    /// <summary>Skärmprofil för bilen (se "avdmanager list device").</summary>
    public string DeviceProfile { get; set; } = "automotive_1024p_landscape";

    public int EmulatorMemoryMb { get; set; } = 4096;

    /// <summary>Port för emulatorns gRPC-API (bildström och tryck). Skyddas med en nyckel som bara finns på datorn.</summary>
    public int EmulatorGrpcPort { get; set; } = 8554;

    // ----- Läge "Android med Google-appar" (Play Store, Google Maps) – bara för utveckling -----

    /// <summary>Avbild med Googles appar. Googles licens: bara för utveckling, får inte följa med produkten.</summary>
    public string GoogleSystemImage { get; set; } = "system-images;android-34-ext9;android-automotive-playstore;x86_64";

    /// <summary>Egen virtuell bil för Google-läget (den vanliga lämnas orörd).</summary>
    public string GoogleAvdName { get; set; } = "Cyberdrome_Automotive_Google";

    public string GoogleDeviceProfile { get; set; } = "automotive_1408p_landscape_with_play";

    /// <summary>Vald skärmstorlek (se ScreenPresets). Standard: 10,1" 1280×800.</summary>
    public string ScreenPresetId { get; set; } = ScreenPresets.DefaultId;

    [JsonConverter(typeof(JsonStringEnumConverter<ScreenOrientation>))]
    public ScreenOrientation ScreenOrientation { get; set; } = ScreenOrientation.Landscape;

    /// <summary>Anpassad storlek (används när ScreenPresetId = "custom").</summary>
    public int CustomScreenWidth { get; set; } = 1280;
    public int CustomScreenHeight { get; set; } = 800;
    public int CustomScreenDpi { get; set; } = 160;

    /// <summary>Upplösning och dpi som gäller just nu (förval + orientering).</summary>
    public (int Width, int Height, int Dpi) EffectiveScreen()
    {
        var p = ScreenPresets.Find(ScreenPresetId);
        if (p.Id == ScreenPresets.CustomId && ScreenPresets.IsValid(CustomScreenWidth, CustomScreenHeight, CustomScreenDpi))
            p = p with { Width = CustomScreenWidth, Height = CustomScreenHeight, Dpi = CustomScreenDpi };
        var (w, h) = p.SizeFor(ScreenOrientation);
        return (w, h, p.Dpi);
    }

    /// <summary>Visa även emulatorns eget fönster (skärmen visas annars bara inne i Cyberdrome).</summary>
    public bool ShowEmulatorWindow { get; set; } = false;

    /// <summary>Var skärmbilder sparas.</summary>
    public string ScreenshotFolder { get; set; } = ScreenshotNaming.DefaultFolder;

    /// <summary>Hur länge vi väntar på att Android ska starta klart.</summary>
    public int BootTimeoutSeconds { get; set; } = 300;

    [JsonIgnore] public string EmulatorExe => Path.Combine(SdkRoot, "emulator", "emulator.exe");
    [JsonIgnore] public string AdbExe => Path.Combine(SdkRoot, "platform-tools", "adb.exe");
    [JsonIgnore] public string SdkManager => Path.Combine(SdkRoot, "cmdline-tools", "latest", "bin", "sdkmanager.bat");
    [JsonIgnore] public string AvdManager => Path.Combine(SdkRoot, "cmdline-tools", "latest", "bin", "avdmanager.bat");

    /// <summary>Mappen där avbilden hamnar, t.ex. system-images\android-34-ext9\android-automotive\x86_64.</summary>
    [JsonIgnore] public string SystemImageDir => Path.Combine([SdkRoot, .. SystemImage.Split(';')]);

    [JsonIgnore]
    public static string AvdHome => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".android", "avd");

    /// <summary>Javamappen som följer med SDK:n (behövs av sdkmanager/avdmanager), eller null.</summary>
    [JsonIgnore]
    public string? JavaHome
    {
        get
        {
            var jdk = Path.Combine(SdkRoot, "jdk");
            if (!Directory.Exists(jdk)) return null;
            if (File.Exists(Path.Combine(jdk, "bin", "java.exe"))) return jdk;
            return Directory.GetDirectories(jdk).FirstOrDefault(d => File.Exists(Path.Combine(d, "bin", "java.exe")));
        }
    }

    // ================= Spara / läsa =================

    public static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cyberdrome", "android.json");

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static VmSettings FromJson(string json)
    {
        var s = JsonSerializer.Deserialize<VmSettings>(json, JsonOptions) ?? new();
        if (RetiredBuilds.Contains(s.AndroidBuild)) s.AndroidBuild = new VmSettings().AndroidBuild;
        // Version 1.2: gamla filer hade 4 kärnor och mjukvarugrafik som standard – uppgradera en gång.
        if (s.PerformanceVersion < 1)
        {
            if (s.Cpus == 4) s.Cpus = DefaultCpus();
            s.EmulatorGpu = "host";
            s.FastAnimations = true;
            s.PerformanceVersion = CurrentPerformanceVersion;
            s.Migrated = true;
        }
        return s;
    }

    public static VmSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = FromJson(File.ReadAllText(FilePath));
                if (loaded.Migrated) loaded.Save();
                return loaded;
            }
        }
        catch { /* trasig fil: använd standardvärden */ }
        var s = new VmSettings();
        s.PerformanceVersion = CurrentPerformanceVersion;
        s.Save();
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, ToJson());
        }
        catch { /* inte kritiskt */ }
    }
}
