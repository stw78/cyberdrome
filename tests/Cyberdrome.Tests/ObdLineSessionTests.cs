// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Text;
using Cyberdrome.Core;
using Cyberdrome.Core.Profiles;
using Cyberdrome.Obd;

namespace Cyberdrome.Tests;

/// <summary>Radhanteringen som delas av Wi-Fi (TCP) och Bluetooth (SPP).</summary>
public class ObdLineSessionTests
{
    private static VirtualCar Golf() =>
        new(ProfileLoader.LoadFolder(Path.Combine(AppContext.BaseDirectory, "profiles")).First(x => x.Id == "vw-golf8"));

    [Fact]
    public void CommandsSplitAcrossChunksAreJoined()
    {
        var car = Golf();
        car.State.SpeedKmh = 54;
        var s = new ObdLineSession(car, "test");
        Assert.Equal("", s.Feed("AT"));
        Assert.Contains("OK", s.Feed("E0\r"));
        Assert.Equal("", s.Feed("01"));
        Assert.Equal("41 0D 36\r\r>", s.Feed("0D\r"));
    }

    [Fact]
    public void LineFeedsAreIgnoredAndSeveralCommandsInOneChunkWork()
    {
        var car = Golf();
        car.State.SpeedKmh = 100;
        var s = new ObdLineSession(car, "test");
        var r = s.Feed("ATE0\r\n010D\r\n");
        Assert.EndsWith("41 0D 64\r\r>", r);
    }

    [Fact]
    public void TrafficIsReportedWithClientName()
    {
        var entries = new List<ObdTrafficEntry>();
        var s = new ObdLineSession(Golf(), "Bluetooth: Pixel", entries.Add);
        s.Feed("ATE0\r");
        Assert.Contains(entries, e => e.FromApp && e.Client == "Bluetooth: Pixel" && e.Text == "ATE0");
        Assert.Contains(entries, e => !e.FromApp && e.Text == "OK");
        Assert.NotNull(s.LastRequest);
    }

    [Fact]
    public async Task RunAsyncAnswersOverAStream()
    {
        var car = Golf();
        car.State.Rpm = 1850;
        var input = new MemoryStream(Encoding.ASCII.GetBytes("ATE0\r010C\r"));
        var output = new MemoryStream();
        var s = new ObdLineSession(car, "test");
        await s.RunAsync(new Duplex(input, output), CancellationToken.None);
        var text = Encoding.ASCII.GetString(output.ToArray());
        Assert.Contains("41 0C 1C E8", text);
    }

    [Fact]
    public void CleanRemovesEchoAndPrompt() =>
        Assert.Equal("41 0D 36", ObdLineSession.Clean("010D\r41 0D 36\r\r>", "010D"));

    private sealed class Duplex(Stream read, Stream write) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set { } }
        public override void Flush() { }
        public override int Read(byte[] b, int o, int c) => read.Read(b, o, c);
        public override ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken ct = default) => read.ReadAsync(b, ct);
        public override void Write(byte[] b, int o, int c) => write.Write(b, o, c);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> b, CancellationToken ct = default) => write.WriteAsync(b, ct);
        public override long Seek(long o, SeekOrigin s) => 0;
        public override void SetLength(long v) { }
    }
}
