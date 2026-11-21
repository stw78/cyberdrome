// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Runtime.CompilerServices;

namespace Cyberdrome.Tests;

/// <summary>Testerna körs på svenska oavsett datorns språk (många kontrollerar svenska meddelanden).</summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Init() => L.SetLanguage("sv");
}
