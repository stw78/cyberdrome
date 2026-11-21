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
        + (t.Profile is null ? "" : $" · bilmodell {t.Profile}") + (t.Drive is null ? "" : L.F("Tests_Drive0", t.Drive))
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
        TestSummary = L.F("Tests_TheTestIsRunning00", t.DurationSeconds);
        TestSummaryColor = Avalonia.Media.Brush.Parse("#2563EB");
        TestInstruction = L.T("Tests_KeepTheAppInAndroidOpen");
        TestProgress = 0;
        IsTestRunning = true;
        AddLog(new ObdTrafficEntry(DateTime.Now, "program", false, L.F("Tests_TestCaseStarted0", t.Name)));
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
        TestSummary = L.F("Tests_TheTestIsRunning002", Math.Max(0, t.DurationSeconds - elapsed), _testTraffic?.Count(e => e.FromApp) ?? 0);
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
                c.Status switch { CheckStatus.Passed => L.T("Tests_Passed"), CheckStatus.NotDone => L.T("Tests_NotPerformedOptional"), _ => L.T("Tests_Failed") },
                Avalonia.Media.Brush.Parse(c.Status switch { CheckStatus.Passed => "#16A34A", CheckStatus.NotDone => "#B45309", _ => "#DC2626" }),
                c.Detail, string.Join("\n", c.Excerpt)));
        TestSummary = (aborted ? L.T("Tests_Cancelled") : "") + (result.Passed ? L.T("Tests_PASSED") : L.T("Tests_FAILED"))
                      + L.F("Tests_0Of1ChecksPassed2", result.PassedCount, result.Checks.Count, result.RequestCount)
                      + (result.Clients.Count > 0 ? L.T("Tests_From") + string.Join(", ", result.Clients) : L.T("Tests_NoAppAsked"));
        TestSummaryColor = Avalonia.Media.Brush.Parse(result.Passed ? "#16A34A" : "#DC2626");
        try { LastReportPath = TestEvaluator.SaveReport(result); }
        catch (Exception ex) { TestSummary += L.T("Tests_TheReportCouldNotBeSaved") + ex.Message; }
        AddLog(new ObdTrafficEntry(DateTime.Now, "program", false, L.F("Tests_TestCaseFinished01", t.Name, (result.Passed ? L.T("Tests_PASSED") : L.T("Tests_FAILED")))));
    }

    [RelayCommand]
    private void OpenTestReport()
    {
        if (LastReportPath is null) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(LastReportPath) { UseShellExecute = true }); }
        catch (Exception ex) { TestSummary = L.T("Tests_CouldNotOpenTheReport") + ex.Message; }
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
            TestSummary = L.T("Tests_TestCaseImported") + t.Name;
            TestSummaryColor = Avalonia.Media.Brush.Parse("#0F172A");
        }
        catch (Exception ex) { TestSummary = L.T("Cars_CouldNotImport") + ex.Message; TestSummaryColor = Avalonia.Media.Brush.Parse("#DC2626"); }
    }

    public void ExportTestCase(string path)
    {
        if (SelectedTest is null) return;
        try { TestStore.Export(SelectedTest, path); TestSummary = "Exporterat: " + path; TestSummaryColor = Avalonia.Media.Brush.Parse("#0F172A"); }
        catch (Exception ex) { TestSummary = L.T("Cars_CouldNotExport") + ex.Message; }
    }
}
