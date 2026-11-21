// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Diagnostics;
using System.Text;

namespace Cyberdrome.Vm;

public sealed record ProcessResult(int ExitCode, string Output)
{
    public bool Ok => ExitCode == 0;
}

/// <summary>Kör ett externt program (t.ex. wsl.exe) och samlar utskriften rad för rad.</summary>
public static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string file, IEnumerable<string> args, string? stdin = null,
        Action<string>? onLine = null, CancellationToken ct = default,
        IReadOnlyDictionary<string, string>? env = null)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = NeutralDir(file),
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        // wsl.exe skriver annars sina egna meddelanden i UTF-16.
        psi.Environment["WSL_UTF8"] = "1";
        foreach (var (k, v) in env ?? new Dictionary<string, string>()) psi.Environment[k] = v;

        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var all = new StringBuilder();
        void Line(string? l)
        {
            if (l is null) return;
            l = l.Replace("\0", "").TrimEnd('\r');
            lock (all) all.AppendLine(l);
            if (l.Length > 0) onLine?.Invoke(l);
        }
        p.OutputDataReceived += (_, e) => Line(e.Data);
        p.ErrorDataReceived += (_, e) => Line(e.Data);

        try { p.Start(); }
        catch (Exception ex) { return new ProcessResult(-1, ex.Message); }

        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        if (stdin is not null)
        {
            // Linux-skript måste ha \n, inte \r\n.
            await p.StandardInput.WriteAsync(stdin.Replace("\r\n", "\n"));
            p.StandardInput.Close();
        }
        try
        {
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        p.WaitForExit(); // töm bufferten
        return new ProcessResult(p.ExitCode, all.ToString());
    }

    /// <summary>Kör ett program och returnerar stdout som byte (t.ex. en PNG-skärmbild). Null vid fel.</summary>
    public static async Task<byte[]?> RunBinaryAsync(string file, IEnumerable<string> args, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo(file)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = NeutralDir(file),
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = new Process { StartInfo = psi };
        try { p.Start(); }
        catch { return null; }
        _ = p.StandardError.ReadToEndAsync(ct); // töm stderr så processen inte fastnar
        using var ms = new MemoryStream();
        try
        {
            await p.StandardOutput.BaseStream.CopyToAsync(ms, ct);
            await p.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        return p.ExitCode == 0 && ms.Length > 0 ? ms.ToArray() : null;
    }

    /// <summary>Startar ett program som administratör (Windows visar en UAC-fråga).</summary>
    public static async Task<int> RunElevatedAsync(string file, string args)
    {
        var psi = new ProcessStartInfo(file, args) { UseShellExecute = true, Verb = "runas" };
        try
        {
            using var p = Process.Start(psi)!;
            await p.WaitForExitAsync();
            return p.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return -2; // användaren sa nej
        }
    }

    /// <summary>
    /// Arbetsmapp för startade program: programmets egen mapp om sökvägen är fullständig, annars Temp. Aldrig
    /// Cyberdromes programmapp – program som lever kvar (emulator, adb-server) skulle annars låsa den vid uppdatering.
    /// </summary>
    public static string NeutralDir(string file)
    {
        try
        {
            if (Path.IsPathRooted(file) && Path.GetDirectoryName(file) is { } d && Directory.Exists(d)) return d;
        }
        catch { }
        return Path.GetTempPath();
    }
}
