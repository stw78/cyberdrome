// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.App;

public enum AdapterDeviceKind { Spp, Ble }

/// <summary>En Bluetooth-enhet som kan vara en OBD-adapter.</summary>
public sealed record AdapterDevice(string Name, AdapterDeviceKind Kind, string Id, ulong Address, int? Rssi)
{
    public string Label => $"{Name}  ({(Kind == AdapterDeviceKind.Spp ? "Bluetooth" : "Bluetooth LE")}{(Rssi is { } r ? $", {r} dBm" : "")})";
}
