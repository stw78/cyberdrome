// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Jouko Broström

using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cyberdrome.Obd;
using Cyberdrome.Obd.Testing;

namespace Cyberdrome.App.ViewModels;

/// <summary>En rad i resultatlistan.</summary>
public sealed record CheckRow(string Name, string StatusText, Avalonia.Media.IBrush StatusColor, string Detail, string Excerpt);

/// <summary>Sidan Testfall: kör ett testfall mot appen i Android och visa godkänt/underkänt.</summary>
public sealed partial class MainViewModel
{
    private readonly TestStore _testStore = new(TestStore.DefaultFolder);
    private List<ObdTrafficEntry>? _testTraffic;
    private TestCase? _runningTest;
    private DateTime _testStart;
    private DateTime?[] _stepTimes = [];
    private DispatcherTimer? _testTimer;

    public ObservableCollection<TestCase> TestCases { get; } = new();
    public ObservableCollection<CheckRow> TestResultRows { get; } = new();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SelectedTestInfo))] private TestCase? _selectedTest;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanRunTest))] private bool _isTestRunning;
    [ObservableProperty] private double _testProgress;
    [ObservableProperty] private string _testInstruction = "";
    [ObservableProperty] private string _testSummary = "";
    [ObservableProperty] private Avalonia.Media.IBrush _testSummaryColor = Avalonia.Media.Brush.Parse("#0F172A");
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasTestReport))] private string? _lastReportPath;

    public bool CanRunTest => !IsTestRunning;
    public bool HasTestReport => LastReportPath is not null;

    public string SelectedTestInfo => SelectedTest is not { } t ? "" :
        $"{t.Description}\n{t.DurationSeconds:0} s · {t.Steps.Count} steg · {t.Checks.Count} kontroller"
        + (t.Profile is null ? "" : $" · bilmodell {t.Profile}") + (t.Drive is null ? "" : $" · körning \"{t.Drive}\"")
        + "\nKontroller: " + string.Join("; ", t.Checks.Select(c => c.Name + (c.Optional ? " (valfri)" : "")));

    private void InitTests()
    {
        TestCases.Clear();
        foreach (var t in _testStore.LoadAll()) TestCases.Add(t);
        SelectedTest ??= TestCases.FirstOrDefault();
    }

    /// <summary>Anropas från AddLog: spelar in trafiken medan ett test körs.</summary>
    private void RecordTestTraffic(ObdTrafficEntry e) => _testTraffic?.Add(e);

    [RelayCommand]
    private void RunTest()
    {
        if (SelectedTest is not { } t || IsTestRunning || _car is null) return;
        if (t.Profile is { } pid && Profiles.FirstOrDefault(p => p.Id == pid) is { } prof && SelectedProfile != prof)
            SelectedProfile = prof;
        if (IsPlayingDrive) StopDrive();
        ApplyTestValues(t.Values);
        if (t.Drive is { } dn && Drives.FirstOrDefault(d => d.Name == dn) is { } drive) StartPlayback(drive.Drive);

        _runningTest = t;
        _stepTimes = new DateTime?[t.Steps.Count];
        _testTraffic = new List<ObdTrafficEntry>();
        _testStart = DateTime.Now;
        TestResultRows.Clear();
        LastReportPath = null;
        TestSummary = $"Testet körs … ({t.DurationSeconds:0} s)";
        TestSummaryColor = Avalonia.Media.Brush.Parse("#2563EB");
        TestInstruction = "Ha appen i Android öppen och ansluten till OBD.";
        TestProgress = 0;
        IsTestRunning = true;
        AddLog(new ObdTrafficEntry(DateTime.Now, "program", false, $"Testfall startat: {t.Name}"));
        _testTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Background, (_, _) => TestTick());
        _testTimer.Start();
        TestTick();
    }

    [RelayCommand]
    private void StopTest()
    {
        if (!IsTestRunning) return;
        FinishTest(aborted: true);
    }

    private void ApplyTestValues(TestValues? v)
    {
        if (v is null || _car is null) return;
        if (v.Speed is { } s) Speed = s;
        if (v.Rpm is { } r) Rpm = r;
        if (v.Coolant is { } c) Coolant = c;
        if (v.Fuel is { } f) Fuel = f;
        if (v.Ignition is { } i) Ignition = i;
    }

    private void TestTick()
    {
        if (_runningTest is not { } t || _car is null) return;
        double elapsed = (DateTime.Now - _testStart).TotalSeconds;
        TestProgress = Math.Min(100, elapsed / t.DurationSeconds * 100);
        for (int i = 0; i < t.Steps.Count; i++)
        {
            var s = t.Steps[i];
            if (_stepTimes[i] is not null || elapsed < s.At) continue;
            _stepTimes[i] = DateTime.Now;
            switch (s.Action)
            {
                case "set": ApplyTestValues(s.Values); break;
                case "addDtc": _car.State.AddDtc(s.Dtc!.ToUpperInvariant()); break;
                case "removeDtc": _car.State.RemoveDtc(s.Dtc!.ToUpperInvariant()); break;
                case "clearDtcs": _car.State.ClearDtcs(); break;
            }
            TestInstruction = $"Steg {i + 1}: {TestEvaluator.Describe(s)}";
            AddLog(new ObdTrafficEntry(DateTime.Now, "program", false, $"Teststeg {i + 1}: {TestEvaluator.Describe(s)}"));
        }
        TestSummary = $"Testet körs … {Math.Max(0, t.DurationSeconds - elapsed):0} s kvar · {_testTraffic?.Count(e => e.FromApp) ?? 0} OBD-frågor hittills";
        if (elapsed >= t.DurationSeconds) FinishTest(aborted: false);
    }

    private void FinishTest(bool aborted)
    {
        _testTimer?.Stop();
        _testTimer = null;
        var t = _runningTest!;
        var traffic = _testTraffic ?? [];
        _runningTest = null;
        _testTraffic = null;
        IsTestRunning = false;
        TestProgress = 100;
        TestInstruction = "";

        var result = TestEvaluator.Evaluate(t, traffic, _testStart, DateTime.Now, _stepTimes);
        TestResultRows.Clear();
        foreach (var c in result.Checks)
            TestResultRows.Add(new CheckRow(c.Name,
                c.Status switch { CheckStatus.Passed => "✔ Godkänd", CheckStatus.NotDone => "– Ej utförd (valfri)", _ => "✘ Underkänd" },
                Avalonia.Media.Brush.Parse(c.Status switch { CheckStatus.Passed => "#16A34A", CheckStatus.NotDone => "#B45309", _ => "#DC2626" }),
                c.Detail, string.Join("\n", c.Excerpt)));
        TestSummary = (aborted ? "Avbrutet – " : "") + (result.Passed ? "GODKÄNT" : "UNDERKÄNT")
                      + $": {result.PassedCount} av {result.Checks.Count} kontroller godkända · {result.RequestCount} OBD-frågor"
                      + (result.Clients.Count > 0 ? " från " + string.Join(", ", result.Clients) : " (ingen app frågade)");
        TestSummaryColor = Avalonia.Media.Brush.Parse(result.Passed ? "#16A34A" : "#DC2626");
        try { LastReportPath = TestEvaluator.SaveReport(result); }
        catch (Exception ex) { TestSummary += " · Rapporten kunde inte sparas: " + ex.Message; }
        AddLog(new ObdTrafficEntry(DateTime.Now, "program", false, $"Testfall klart: {t.Name} – {(result.Passed ? "GODKÄNT" : "UNDERKÄNT")}"));
    }

    [RelayCommand]
    private void OpenTestReport()
    {
        if (LastReportPath is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LastReportPath) { UseShellExecute = true }); }
        catch (Exception ex) { TestSummary = "Kunde inte öppna rapporten: " + ex.Message; }
    }

    [RelayCommand]
    private void OpenReportFolder()
    {
        try
        {
            Directory.CreateDirectory(TestEvaluator.ReportFolder);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(TestEvaluator.ReportFolder) { UseShellExecute = true });
        }
        catch { }
    }

    public void ImportTestCase(string path)
    {
        try
        {
            var t = _testStore.Import(path);
            InitTests();
            SelectedTest = TestCases.FirstOrDefault(x => x.Id == t.Id);
            TestSummary = "Testfall importerat: " + t.Name;
            TestSummaryColor = Avalonia.Media.Brush.Parse("#0F172A");
        }
        catch (Exception ex) { TestSummary = "Kunde inte importera: " + ex.Message; TestSummaryColor = Avalonia.Media.Brush.Parse("#DC2626"); }
    }

    public void ExportTestCase(string path)
    {
        if (SelectedTest is null) return;
        try { TestStore.Export(SelectedTest, path); TestSummary = "Exporterat: " + path; TestSummaryColor = Avalonia.Media.Brush.Parse("#0F172A"); }
        catch (Exception ex) { TestSummary = "Kunde inte exportera: " + ex.Message; }
    }
}
