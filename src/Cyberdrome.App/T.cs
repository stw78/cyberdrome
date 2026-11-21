// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.ComponentModel;
using Avalonia.Data;
using Avalonia.Markup.Xaml;

namespace Cyberdrome.App;

/// <summary>
/// Översatt text i XAML: Text="{l:T Ui_Start}". Texten byts direkt när språket ändras.
/// </summary>
public sealed class T : MarkupExtension
{
    public T() { }
    public T(string key) => Key = key;

    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new Binding(nameof(LocString.Value)) { Source = LocString.For(Key), Mode = BindingMode.OneWay };
}

/// <summary>En översatt text som meddelar när språket byts (en instans per nyckel).</summary>
public sealed class LocString : INotifyPropertyChanged
{
    private static readonly Dictionary<string, LocString> Cache = new();
    private readonly string _key;

    private LocString(string key)
    {
        _key = key;
        L.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }

    public static LocString For(string key)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var s)) Cache[key] = s = new LocString(key);
            return s;
        }
    }

    public string Value => L.T(_key);
    public event PropertyChangedEventHandler? PropertyChanged;
}
