// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace Cyberdrome.Localization;

/// <summary>
/// Programmets texter. Engelska är reserv (Strings.resx); svenska finns i Strings.sv.resx. Fler språk: lägg till
/// Strings.&lt;kod&gt;.resx (se CONTRIBUTING.md). Språket kan bytas medan programmet kör (<see cref="Changed"/>).
/// </summary>
public static class L
{
    public const string SystemLanguage = "system";
    /// <summary>Språk som finns (kod → namn på språket självt).</summary>
    public static readonly IReadOnlyDictionary<string, string> Languages = new Dictionary<string, string>
    {
        ["sv"] = "Svenska",
        ["en"] = "English",
    };

    private static readonly ResourceManager Rm = new("Cyberdrome.Localization.Strings", typeof(L).Assembly);
    private static CultureInfo _culture = Pick(SystemLanguage);

    /// <summary>Körs när språket byts (gränssnittet uppdaterar sina texter).</summary>
    public static event Action? Changed;

    public static CultureInfo Culture => _culture;

    /// <summary>"sv", "en" eller "system" (systemets språk: svenska om Windows är på svenska, annars engelska).</summary>
    public static CultureInfo Pick(string language)
    {
        if (language == SystemLanguage || string.IsNullOrWhiteSpace(language))
        {
            var sys = CultureInfo.InstalledUICulture;
            return Languages.ContainsKey(sys.TwoLetterISOLanguageName) && sys.TwoLetterISOLanguageName != "en"
                ? CultureInfo.GetCultureInfo(sys.TwoLetterISOLanguageName)
                : CultureInfo.GetCultureInfo("en");
        }
        return CultureInfo.GetCultureInfo(language);
    }

    /// <summary>Byter språk för texter och för formatering av tal och datum.</summary>
    public static void SetLanguage(string language)
    {
        var c = Pick(language);
        var fmt = c.Name == "en" ? CultureInfo.GetCultureInfo("en-GB") : c; // 24-timmarsklocka och dag/månad/år
        _culture = c;
        CultureInfo.CurrentUICulture = c;
        CultureInfo.DefaultThreadCurrentUICulture = c;
        CultureInfo.CurrentCulture = fmt;
        CultureInfo.DefaultThreadCurrentCulture = fmt;
        Changed?.Invoke();
    }

    /// <summary>Text för nyckeln. Saknas den visas nyckeln (lätt att upptäcka).</summary>
    public static string T(string key) => Rm.GetString(key, _culture) ?? key;

    /// <summary>Text med värden: "{0} km/h". Formateras enligt valt språk.</summary>
    public static string F(string key, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    /// <summary>Text i ett bestämt språk (för tester och export).</summary>
    public static string? In(string key, string language) => Rm.GetString(key, CultureInfo.GetCultureInfo(language));

    /// <summary>Alla nycklar i ett språk (bara den filen, utan reserv).</summary>
    public static IReadOnlyDictionary<string, string> All(string language)
    {
        var set = Rm.GetResourceSet(language == "en" ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(language), true, false);
        var d = new Dictionary<string, string>();
        if (set is null) return d;
        foreach (System.Collections.DictionaryEntry e in set) d[(string)e.Key] = (string)e.Value!;
        return d;
    }
}

/// <summary>Bindningsbar översättare för gränssnittet: Loc.Instance["Nyckel"]. Meddelar ändring när språket byts.</summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();
    private Loc() => L.Changed += () =>
    {
        // Avalonia lyssnar på olika namn för indexerare beroende på bindningstyp; skicka alla.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    };
    public string this[string key] => L.T(key);
    public event PropertyChangedEventHandler? PropertyChanged;
}
