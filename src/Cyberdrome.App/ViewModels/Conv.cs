// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using Avalonia.Data.Converters;

namespace Cyberdrome.App.ViewModels;

public static class Conv
{
    public static readonly IValueConverter OnOff =
        new FuncValueConverter<bool, string>(b => b ? "Fungerar" : "Av");

    public static readonly IValueConverter ConnectText =
        new FuncValueConverter<bool, string>(b => b ? "Koppla från" : "Anslut");
}
