// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

namespace Cyberdrome.Core.Profiles;

/// <summary>
/// En tillverkarspecifik fråga (t.ex. UDS 22 1002 till 700) med en eller flera signaler i svaret – samma modell som
/// OBDb. Simulatorn svarar med signalernas värden kodade enligt formeln (värde = rå × mul / div + add).
/// </summary>
public sealed class ExtraCommand
{
    /// <summary>CAN-id för frågan (styrdon), t.ex. "700".</summary>
    public string Header { get; set; } = "7E0";
    /// <summary>Fråga i hex: tjänst + id, t.ex. "221002".</summary>
    public string Request { get; set; } = "";
    /// <summary>Var signalerna kommer från, t.ex. "OBDb".</summary>
    public string? Source { get; set; }
    public List<ExtraSignal> Signals { get; set; } = [];

    /// <summary>Hela svaret: positiv tjänst (+0x40), id-byten och signalbyten.</summary>
    public byte[] BuildResponse()
    {
        var req = Convert.FromHexString(Request);
        int bits = Signals.Count == 0 ? 8 : Signals.Max(s => s.Bix + s.Len);
        var data = new byte[(bits + 7) / 8];
        foreach (var s in Signals) s.Write(data);
        return [(byte)(req[0] + 0x40), .. req[1..], .. data];
    }
}

public sealed class ExtraSignal
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    /// <summary>Bitposition i svaret efter tjänst + id (0 = första biten, mest signifikant först).</summary>
    public int Bix { get; set; }
    public int Len { get; set; } = 8;
    public double Mul { get; set; } = 1;
    public double Div { get; set; } = 1;
    public double Add { get; set; }
    public double Min { get; set; }
    public double Max { get; set; } = 255;
    public bool Signed { get; set; }
    /// <summary>Värdet simulatorn rapporterar (i signalens enhet).</summary>
    public double Value { get; set; }

    /// <summary>Rått heltal för <see cref="Value"/>: (värde − add) × div / mul, begränsat till bitlängden.</summary>
    public long Raw()
    {
        double mul = Mul == 0 ? 1 : Mul;
        var raw = (long)Math.Round((Value - Add) * Div / mul);
        int len = Math.Clamp(Len, 1, 63);
        long lo = Signed ? -(1L << (len - 1)) : 0, hi = Signed ? (1L << (len - 1)) - 1 : (1L << len) - 1;
        return Math.Clamp(raw, lo, hi);
    }

    /// <summary>Avkodar ett svar (för tester och visning).</summary>
    public double Decode(byte[] data)
    {
        long raw = 0;
        for (int i = 0; i < Len; i++)
        {
            int bit = Bix + i;
            // En bit (0 eller 1) som byte: inget teckenutvidgat int-värde blandas in i long (CS0675).
            byte b = (byte)((data[bit / 8] >> (7 - bit % 8)) & 1);
            raw = (raw << 1) | b;
        }
        if (Signed && Len < 64 && (raw & (1L << (Len - 1))) != 0) raw -= 1L << Len;
        return raw * Mul / (Div == 0 ? 1 : Div) + Add;
    }

    public void Write(byte[] data)
    {
        long raw = Raw();
        int len = Math.Clamp(Len, 1, 63);
        for (int i = 0; i < len; i++)
        {
            int bit = Bix + i;
            if (bit / 8 >= data.Length) break;
            bool on = ((raw >> (len - 1 - i)) & 1) != 0;
            if (on) data[bit / 8] |= (byte)(0x80 >> (bit % 8));
            else data[bit / 8] &= (byte)~(0x80 >> (bit % 8));
        }
    }
}
