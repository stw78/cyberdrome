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

    private sealed class FormatConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            parameter is string key ? L.F(key, value) : value?.ToString();

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
