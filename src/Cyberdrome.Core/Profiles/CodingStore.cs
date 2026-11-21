// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Core.Profiles;

/// <summary>
/// Håller den levande kodningen för alla ECU:er i den valda bilen.
/// UI och OBD (UDS 0x22/0x2E) läser och skriver samma data.
/// </summary>
public sealed class CodingStore
{
    private readonly object _lock = new();
    private readonly Dictionary<string, byte[]> _coding = new();

    public CarProfile Profile { get; }

    /// <summary>
    /// Om skrivning av kodning (UDS 2E) kräver upplåsning med säkerhetsnyckel (UDS 27).
    /// Kan stängas av i programmet för tester. Standard: på.
    /// </summary>
    public bool RequireSecurityAccess { get; set; } = true;

    /// <summary>Formeln som gäller för ett styrdon: styrdonets egen, annars bilens, annars standard.</summary>
    public SecurityAccessConfig SecurityFor(EcuProfile ecu) =>
        ecu.SecurityAccess ?? Profile.SecurityAccess ?? SecurityAccessConfig.Default;

    public event EventHandler? Changed;

    public CodingStore(CarProfile profile)
    {
        Profile = profile;
        foreach (var ecu in profile.Ecus)
            _coding[ecu.Key] = Convert.FromHexString(ecu.Coding);
    }

    public byte[] Get(string ecuKey) { lock (_lock) return (byte[])_coding[ecuKey].Clone(); }

    /// <summary>Skriver ny kodning. Längden måste vara samma som förut.</summary>
    public bool TrySet(string ecuKey, byte[] data)
    {
        lock (_lock)
        {
            if (!_coding.TryGetValue(ecuKey, out var old) || old.Length != data.Length) return false;
            _coding[ecuKey] = (byte[])data.Clone();
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public bool IsOn(CodingFeature f)
    {
        lock (_lock) return (_coding[f.Ecu][f.Byte] & (1 << f.Bit)) != 0;
    }

    public void SetFeature(CodingFeature f, bool on)
    {
        lock (_lock)
        {
            var b = _coding[f.Ecu];
            b[f.Byte] = on ? (byte)(b[f.Byte] | (1 << f.Bit)) : (byte)(b[f.Byte] & ~(1 << f.Bit));
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Återställ till fabriksläge från profilfilen.</summary>
    public void Reset()
    {
        lock (_lock)
            foreach (var ecu in Profile.Ecus)
                _coding[ecu.Key] = Convert.FromHexString(ecu.Coding);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
