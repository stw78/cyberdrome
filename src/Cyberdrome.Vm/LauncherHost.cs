// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Vm;

/// <summary>
/// Android-läge där Cyberdrome Launcher (bilens tema) kan installeras och sättas som startskärm.
/// Finns i emulatorlägena och i WSL2 + Cuttlefish.
/// </summary>
public interface ILauncherHost
{
    /// <summary>Namn som skiljer de virtuella bilarna åt (för att minnas vilken Launcher-version som installerats).</summary>
    string LauncherKey { get; }
    Task<bool> IsInstalledAsync(string packageName, CancellationToken ct = default);
    Task<ProcessResult> InstallApkAsync(string windowsPath, Action<string> log, CancellationToken ct = default);
    Task<ProcessResult> RunShellAsync(string[] args, CancellationToken ct = default);
    Task<bool> SetHomeAsync(string component, CancellationToken ct = default);
    Task<bool> ResetHomeAsync(CancellationToken ct = default);
}
