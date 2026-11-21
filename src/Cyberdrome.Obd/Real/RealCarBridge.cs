// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text.Json;
using Cyberdrome.Core;

namespace Cyberdrome.Obd.Real;

/// <summary>En skrivning som användaren måste godkänna.</summary>
public sealed record WriteRequest(int Header, byte[] Request, string Service, string? BackupFile, double? Voltage)
{
    public string Ecu => $"{Header:X3}";
    public string DataHex => Convert.ToHexString(Request);
}

/// <summary>
/// Vidarekoppling: appar i Android (Wi-Fi, Bluetooth, BLE) pratar med den riktiga bilen genom adaptern.
/// Läsläge som standard. Skrivning kräver att användaren slår på "Tillåt skrivning" för sessionen, att
/// batterispänningen är minst 12,0 V, att originalvärdet säkerhetskopieras först och en bekräftelse per skrivning.
/// </summary>
public sealed class RealCarBridge : IObdBridge
{
    public const double MinWriteVoltage = 12.0;
    private readonly ObdAdapter _adapter;
    private readonly Func<WriteRequest, Task<bool>> _confirm;

    public RealCarBridge(ObdAdapter adapter, string backupFolder, Func<WriteRequest, Task<bool>> confirm)
    {
        _adapter = adapter;
        BackupFolder = backupFolder;
        _confirm = confirm;
    }

    public static string DefaultBackupFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create), "Cyberdrome", "backup");

    public string BackupFolder { get; }

    /// <summary>"Tillåt skrivning" för den här sessionen. Av som standard; nollställs när adaptern kopplas från.</summary>
    public bool AllowWrites { get; set; }

    public double? Voltage => _adapter.LastVoltage;
    public string? Protocol => _adapter.Info?.Protocol;

    public BridgeReply Request(int header, byte[] request) => RequestAsync(header, request).GetAwaiter().GetResult();

    private static BridgeReply Negative(int header, byte sid, string hint) =>
        new([new EcuReply(header == ObdAdapter.FunctionalHeader ? 0x7E8 : header + 8, [0x7F, sid, 0x22])], null, hint); // 22 = conditionsNotCorrect

    public async Task<BridgeReply> RequestAsync(int header, byte[] request)
    {
        var d = WriteFilter.Classify(request);
        byte sid = request.Length > 0 ? request[0] : (byte)0;
        if (d.Kind != FilterKind.Read)
        {
            if (!AllowWrites)
                return Negative(header, sid, L.F("RealCarBridge_BlockedReadOnly0To1", d.Service, header));
            if (d.Kind == FilterKind.Write)
            {
                double? volt = null;
                try { volt = await _adapter.ReadVoltageAsync().ConfigureAwait(false); } catch { }
                if (volt is null or < MinWriteVoltage)
                    return Negative(header, sid, L.F("RealCarBridge_BlockedTheBatteryVoltageIs0", (volt is { } v ? v.ToString("0.0") + " V" : L.T("Bridge_Unknown")), MinWriteVoltage));
                string? backup;
                try { backup = await BackupAsync(header, request, d.Service, volt).ConfigureAwait(false); }
                catch (Exception ex) { return Negative(header, sid, L.T("RealCarBridge_BlockedCouldNotBackUpThe") + ex.Message); }
                bool ok;
                try { ok = await _confirm(new WriteRequest(header, request, d.Service, backup, volt)).ConfigureAwait(false); }
                catch { ok = false; }
                if (!ok) return Negative(header, sid, L.F("RealCarBridge_DeniedByTheUser0To", d.Service, header));
                var (wr, we) = await _adapter.RequestAsync(header, request).ConfigureAwait(false);
                return new BridgeReply(wr, we, L.F("RealCarBridge_SentToTheCar0To", d.Service, header, Convert.ToHexString(request), backup));
            }
            // 0x27: nyckeln kommer från appen; Cyberdrome räknar aldrig ut riktiga nycklar.
        }
        var (replies, error) = await _adapter.RequestAsync(header, request).ConfigureAwait(false);
        return new BridgeReply(replies, error);
    }

    /// <summary>
    /// Läser originalvärdet innan en skrivning och sparar det i backup-mappen.
    /// 2E → läs samma DID (22), 3B → läs samma id (21), 04/14 → läs felkoderna, övrigt → logga vad som skickas.
    /// </summary>
    public async Task<string> BackupAsync(int header, byte[] request, string service, double? voltage)
    {
        Directory.CreateDirectory(BackupFolder);
        byte[]? readReq = request[0] switch
        {
            0x2E when request.Length >= 3 => [0x22, request[1], request[2]],
            0x3B when request.Length >= 2 => [0x21, request[1]],
            0x04 => [0x03],
            0x14 => [0x19, 0x02, 0xFF],
            _ => null,
        };
        string? original = null, readError = null;
        if (readReq is not null)
        {
            var (r, e) = await _adapter.RequestAsync(header, readReq).ConfigureAwait(false);
            if (r.Count > 0) original = string.Join(" | ", r.Select(x => $"{x.ResponseId:X3}: {Convert.ToHexString(x.Data)}"));
            else readError = e ?? "NO DATA";
            if (request[0] is 0x2E or 0x3B && (original is null || r.All(x => x.Data.Length > 0 && x.Data[0] == 0x7F)))
                throw new InvalidOperationException(L.F("RealCarBridge_TheOriginalValueCouldNotBe", readError ?? original));
        }
        var file = Path.Combine(BackupFolder, $"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{header:X3}_{request[0]:X2}.json");
        var json = JsonSerializer.Serialize(new
        {
            tid = DateTime.Now.ToString("o"),
            styrdon = $"{header:X3}",
            tjanst = service,
            skrivning = Convert.ToHexString(request),
            lasfraga = readReq is null ? null : Convert.ToHexString(readReq),
            original,
            lasfel = readError,
            spanning = voltage,
            adapter = _adapter.Info?.Version,
        }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        await File.WriteAllTextAsync(file, json).ConfigureAwait(false);
        return file;
    }
}
