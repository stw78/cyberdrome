// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.ComponentModel;

namespace Cyberdrome.App.ViewModels;

/// <summary>En sida i menyn. Namnet byts direkt när språket ändras.</summary>
public sealed class PageItem : INotifyPropertyChanged
{
    public PageItem(string key)
    {
        Key = key;
        L.Changed += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
    }

    public string Key { get; }
    public string Name => L.T(Key);
    public event PropertyChangedEventHandler? PropertyChanged;
}
