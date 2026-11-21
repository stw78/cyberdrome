// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.App;

/// <summary>Donationer till utvecklingen (PayPal). Länken finns bara här (och i README).</summary>
public static class Donation
{
    /// <summary>PayPal-länk för donationer. Visas i Om-fönstret ("Stöd utvecklingen").</summary>
    public const string Url = "https://www.paypal.me/stw78";

    public static bool Available => Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
