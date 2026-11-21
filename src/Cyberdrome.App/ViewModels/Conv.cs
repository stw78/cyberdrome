// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Globalization;
using Avalonia.Data.Converters;

namespace Cyberdrome.App.ViewModels;

public static class Conv
{
    public static readonly IValueConverter OnOff =
        new FuncValueConverter<bool, string>(b => b ? L.T("Conv_Working") : L.T("Conv_Off"));

    public static readonly IValueConverter ConnectText =
        new FuncValueConverter<bool, string>(b => b ? L.T("Conv_Disconnect") : L.T("Conv_Connect"));

    /// <summary>Översatt format: ConverterParameter = nyckel med {0}, t.ex. "Fmt_Selected" → "Vald: {0}".</summary>
    public static readonly IValueConverter Format = new FormatConverter();

    private static readonly Avalonia.Media.IBrush DotOn = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#34C759"));
    private static readonly Avalonia.Media.IBrush DotOff = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#AEAEB2"));

    /// <summary>Grön punkt när något är igång, grå annars (statusraden).</summary>
    public static readonly IValueConverter DotBrush = new FuncValueConverter<bool, Avalonia.Media.IBrush>(b => b ? DotOn : DotOff);

    private sealed class FormatConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            parameter is string key ? L.F(key, value) : value?.ToString();

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
