using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using WorshipLoopStudio.App.Infrastructure;
using WorshipLoopStudio.App.Services;
using WorshipLoopStudio.Audio.Engine;
using WorshipLoopStudio.Audio.IO;
using WorshipLoopStudio.Core.Projects;
using WorshipLoopStudio.Core.Samples;
using WorshipLoopStudio.Core.Timing;

namespace WorshipLoopStudio.App.ViewModels;

public sealed record OptionItem<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

public sealed class BeatLedViewModel : ObservableObject
{
    private bool _isActive;

    public BeatLedViewModel(int number, bool isAccent)
    {
        Number = number;
        IsAccent = isAccent;
    }

    public int Number { get; }
    public bool IsAccent { get; }

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}

/// <summary>The live performance screen: transport, tempo, sequencer, mixer and sample browser.</summary>
public sealed class PerformViewModel : ObservableObject
{
    private readonly AppServices _s;
    private readonly List<double> _taps = new();
    private readonly Stopwatch _tapClock = Stopwatch.StartNew();
    private IReadOnlyList<SampleEntry> _allSamples = Array.Empty<SampleEntry>();
    private Project _project = Project.CreateDefault("Untitled");
    private string? _projectPath;
    private bool _isDirty;
    private int _lastStep = -1;
    private EngineTransport _transport;
    private string _positionText = "Stopped";
    private double _masterLevelL;
    private double _masterLevelR;
    private TrackViewModel? _selectedTrack;
    private string _selectedCategory = SampleLibrary.AllCategory;
    private string _searchText = string.Empty;
    private SampleEntry? _selectedSample;
    private OptionItem<StopMode> _selectedStopMode;
    private bool _recordPads;
    private double _previewVolume = 0.8;
    private int _exportLoops = 4;
    private int _sampleLoadGeneration;

    public PerformViewModel(AppServices services)
    {
        _s = services;
        StopModes = new[]
        {
            new OptionItem<StopMode>(StopMode.EndOfBar, "Stop at end of bar"),
            new OptionItem<StopMode>(StopMode.EndOfLoop, "Stop at end of loop"),
            new OptionItem<StopMode>(StopMode.Release, "Stop now (let ring)"),
            new OptionItem<StopMode>(StopMode.Immediate, "Stop now (cut)"),
        };
        _selectedStopMode = StopModes.FirstOrDefault(o => o.Value.ToString() == _s.Settings.StopMode) ?? StopModes[0];
        Quantizations = Enum.GetValues<Quantization>().Select(q => new OptionItem<Quantization>(q, q.DisplayName())).ToArray();

        PlayCommand = new RelayCommand(Play);
        StopCommand = new RelayCommand(Stop);
        TogglePlayCommand = new RelayCommand(TogglePlay);
        PanicCommand = new RelayCommand(Panic);
        NudgeBpmCommand = new RelayCommand(p => NudgeBpm(p));
        TapTempoCommand = new RelayCommand(TapTempo);
        PreviewCommand = new AsyncRelayCommand(p => PreviewAsync(p as SampleEntry ?? SelectedSample));
        StopPreviewCommand = new RelayCommand(() => _s.Engine.StopPreview());
        AddTrackCommand = new RelayCommand(p => AddTrackFromSample(p as SampleEntry ?? SelectedSample), _ => SelectedSample is not null && Tracks.Count < Project.MaxTracks);
        AssignSampleCommand = new RelayCommand(() => AssignSample(), () => SelectedSample is not null && SelectedTrack is not null);
        ImportSamplesCommand = new AsyncRelayCommand(ImportSamplesAsync);
        RefreshLibraryCommand = new RelayCommand(RefreshLibrary);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
    }

    public event EventHandler? DirtyChanged;

    public ObservableCollection<TrackViewModel> Tracks { get; } = new();
    public ObservableCollection<BeatLedViewModel> BeatLeds { get; } = new();
    public ObservableCollection<string> Categories { get; } = new();
    public ObservableCollection<SampleEntry> VisibleSamples { get; } = new();

    public IReadOnlyList<TimeSignature> TimeSignatures => TimeSignature.Common;
    public IReadOnlyList<OptionItem<Quantization>> Quantizations { get; }
    public IReadOnlyList<int> BarOptions => Project.AllowedBars;
    public IReadOnlyList<int> CountInOptions { get; } = new[] { 0, 1, 2 };
    public IReadOnlyList<int> ExportLoopOptions { get; } = new[] { 1, 2, 4, 8, 16, 32 };
    public IReadOnlyList<OptionItem<StopMode>> StopModes { get; }

    public ICommand PlayCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand TogglePlayCommand { get; }
    public ICommand PanicCommand { get; }
    public ICommand NudgeBpmCommand { get; }
    public ICommand TapTempoCommand { get; }
    public ICommand PreviewCommand { get; }
    public ICommand StopPreviewCommand { get; }
    public ICommand AddTrackCommand { get; }
    public ICommand AssignSampleCommand { get; }
    public ICommand ImportSamplesCommand { get; }
    public ICommand RefreshLibraryCommand { get; }
    public ICommand ClearSearchCommand { get; }

    public Project Project => _project;

    public string? ProjectPath
    {
        get => _projectPath;
        set
        {
            if (SetProperty(ref _projectPath, value))
            {
                OnPropertyChanged(nameof(ProjectName));
            }
        }
    }

    public string ProjectName => _project.Name;

    public string? ProjectDirectory => ProjectPath is null ? null : Path.GetDirectoryName(ProjectPath);

    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (SetProperty(ref _isDirty, value))
            {
                DirtyChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    // ---------------------------------------------------------------- tempo & grid

    public double Bpm
    {
        get => _project.Bpm;
        set
        {
            var v = TempoMath.ClampBpm(value);
            if (Math.Abs(_project.Bpm - v) > 0.0001)
            {
                _project.Bpm = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LoopLengthText));
                MarkChanged(true);
            }
            else
            {
                OnPropertyChanged();
            }
        }
    }

    public TimeSignature SelectedTimeSignature
    {
        get => _project.GetTimeSignature();
        set
        {
            if (value != _project.GetTimeSignature() && value.IsValid)
            {
                ChangeGrid(() => _project.SetTimeSignature(value));
                OnPropertyChanged();
            }
        }
    }

    public OptionItem<Quantization> SelectedQuantization
    {
        get => Quantizations.First(q => q.Value == _project.Quantization);
        set
        {
            if (value is not null && value.Value != _project.Quantization)
            {
                ChangeGrid(() => _project.Quantization = value.Value);
                OnPropertyChanged();
            }
        }
    }

    public int Bars
    {
        get => _project.Bars;
        set
        {
            if (value != _project.Bars && Project.AllowedBars.Contains(value))
            {
                ChangeGrid(() => _project.Bars = value);
                OnPropertyChanged();
            }
        }
    }

    public string LoopLengthText =>
        $"{_project.Bars} bar{(_project.Bars > 1 ? "s" : string.Empty)} = {TempoMath.LoopDurationSeconds(_project.Bpm, _project.GetTimeSignature(), _project.Bars):0.00} s";

    // ---------------------------------------------------------------- metronome

    public bool MetronomeEnabled
    {
        get => _project.Metronome.Enabled;
        set
        {
            if (_project.Metronome.Enabled != value)
            {
                _project.Metronome.Enabled = value;
                OnPropertyChanged();
                MarkChanged(true);
            }
        }
    }

    public double MetronomeVolume
    {
        get => _project.Metronome.Volume;
        set
        {
            var v = Math.Clamp(value, 0, 1);
            if (Math.Abs(_project.Metronome.Volume - v) > 1e-6)
            {
                _project.Metronome.Volume = v;
                OnPropertyChanged();
                MarkChanged(true);
            }
        }
    }

    public int CountInBars
    {
        get => _project.Metronome.CountInBars;
        set
        {
            if (_project.Metronome.CountInBars != value)
            {
                _project.Metronome.CountInBars = Math.Clamp(value, 0, 4);
                OnPropertyChanged();
                MarkChanged(true);
            }
        }
    }

    public double MasterVolume
    {
        get => _project.MasterVolume;
        set
        {
            var v = Math.Clamp(value, 0, 1.5);
            if (Math.Abs(_project.MasterVolume - v) > 1e-6)
            {
                _project.MasterVolume = v;
                OnPropertyChanged();
                MarkChanged(true);
            }
        }
    }

    public double PreviewVolume
    {
        get => _previewVolume;
        set => SetProperty(ref _previewVolume, Math.Clamp(value, 0, 1));
    }

    public int ExportLoops
    {
        get => _exportLoops;
        set => SetProperty(ref _exportLoops, value);
    }

    // ---------------------------------------------------------------- transport state

    public OptionItem<StopMode> SelectedStopMode
    {
        get => _selectedStopMode;
        set
        {
            if (value is not null && SetProperty(ref _selectedStopMode, value))
            {
                _s.Settings.StopMode = value.Value.ToString();
            }
        }
    }

    public bool IsPlaying => _transport == EngineTransport.Playing;
    public bool IsCountingIn => _transport == EngineTransport.CountIn;
    public bool IsStopped => _transport == EngineTransport.Stopped;

    public string TransportText => _transport switch
    {
        EngineTransport.Playing => "PLAYING",
        EngineTransport.CountIn => $"COUNT-IN {_s.Engine.CountInBeatsRemaining}",
        _ => "STOPPED",
    };

    public string PositionText
    {
        get => _positionText;
        private set => SetProperty(ref _positionText, value);
    }

    public double MasterLevelL
    {
        get => _masterLevelL;
        private set => SetProperty(ref _masterLevelL, value);
    }

    public double MasterLevelR
    {
        get => _masterLevelR;
        private set => SetProperty(ref _masterLevelR, value);
    }

    public bool RecordPads
    {
        get => _recordPads;
        set => SetProperty(ref _recordPads, value);
    }

    public TrackViewModel? SelectedTrack
    {
        get => _selectedTrack;
        set
        {
            var old = _selectedTrack;
            if (SetProperty(ref _selectedTrack, value))
            {
                if (old is not null) old.IsSelected = false;
                if (value is not null) value.IsSelected = true;
            }
        }
    }

    // ---------------------------------------------------------------- library

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value ?? SampleLibrary.AllCategory))
            {
                FilterSamples();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value ?? string.Empty))
            {
                FilterSamples();
            }
        }
    }

    public SampleEntry? SelectedSample
    {
        get => _selectedSample;
        set => SetProperty(ref _selectedSample, value);
    }

    // ================================================================= project

    public void LoadProject(Project project, string? path)
    {
        _s.Engine.Stop(StopMode.Immediate);
        project.Normalize();
        _project = project;
        ProjectPath = path;
        Tracks.Clear();
        foreach (var model in project.Tracks)
        {
            Tracks.Add(new TrackViewModel(this, model));
        }

        RebuildGridViews();
        SelectedTrack = Tracks.FirstOrDefault();
        OnPropertyChanged(string.Empty);
        RebuildArrangement();
        _ = LoadSamplesAsync();
        IsDirty = false;
    }

    public void RenameProject(string name)
    {
        _project.Name = name;
        OnPropertyChanged(nameof(ProjectName));
    }

    public void MarkChanged(bool rebuildAudio)
    {
        IsDirty = true;
        if (rebuildAudio)
        {
            RebuildArrangement();
        }
    }

    public EngineArrangement BuildArrangement() => ArrangementBuilder.Build(_project, SampleFor);

    public void RebuildArrangement() => _s.Engine.SetArrangement(BuildArrangement());

    public IReadOnlyList<string> MissingSampleNames() =>
        Tracks.Where(t => t.IsMissing).Select(t => $"{t.Name} ({t.SampleName})").ToList();

    private CachedSample SampleFor(TrackModel model)
    {
        var path = SampleRefs.Resolve(model.SampleRef, _s.Paths, ProjectDirectory);
        return path is not null && _s.Samples.TryGetLoaded(path, out var sample) ? sample : CachedSample.Empty;
    }

    /// <summary>Decodes every sample used by the project in the background, then refreshes the engine snapshot.</summary>
    public async Task LoadSamplesAsync()
    {
        var generation = ++_sampleLoadGeneration;
        var missing = new List<string>();
        foreach (var track in Tracks.ToList())
        {
            var path = SampleRefs.Resolve(track.Model.SampleRef, _s.Paths, ProjectDirectory);
            if (path is null)
            {
                track.IsMissing = true;
                track.Problem = "No sample assigned. Pick a sound in the browser and click 'Assign'.";
                continue;
            }

            try
            {
                await _s.Samples.GetAsync(path);
                track.IsMissing = false;
                track.Problem = null;
            }
            catch (Exception ex)
            {
                track.IsMissing = true;
                track.Problem = ex.Message;
                missing.Add(track.Name);
                _s.Log.Warn($"Sample missing for track {track.Name}: {path}", ex);
            }
        }

        if (generation != _sampleLoadGeneration)
        {
            return;
        }

        RebuildArrangement();
        if (missing.Count > 0)
        {
            ErrorReporter.Report($"{missing.Count} track(s) have a missing or unreadable sample: {string.Join(", ", missing)}. They play silence until you assign another sample.", null);
        }
    }

    private void ChangeGrid(Action apply)
    {
        var oldGrid = _project.GetGrid();
        apply();
        var newGrid = _project.GetGrid();
        foreach (var track in _project.Tracks)
        {
            track.Steps = PatternRemapper.Remap(track.Steps, oldGrid, newGrid, repeatToFill: true);
        }

        RebuildGridViews();
        OnPropertyChanged(nameof(LoopLengthText));
        MarkChanged(true);
    }

    private void RebuildGridViews()
    {
        var grid = _project.GetGrid();
        foreach (var track in Tracks)
        {
            track.RebuildSteps(grid);
        }

        BeatLeds.Clear();
        for (var i = 0; i < grid.BeatsPerBar; i++)
        {
            BeatLeds.Add(new BeatLedViewModel(i + 1, i == 0));
        }

        _lastStep = -1;
    }

    // ================================================================= tracks

    public void AddTrackFromSample(SampleEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        if (Tracks.Count >= Project.MaxTracks)
        {
            ErrorReporter.Report($"A project can have up to {Project.MaxTracks} tracks.", null);
            return;
        }

        var loopCategories = new[] { SampleLibrary.LoopsCategory, SampleLibrary.RecordingsCategory, SampleLibrary.StemsCategory };
        var model = new TrackModel
        {
            Name = ShortName(entry.Name),
            SampleRef = entry.SampleRef,
            Kind = loopCategories.Contains(entry.Category) ? TrackKind.AudioLoop : TrackKind.Pattern,
        };
        AddTrack(model);
    }

    public void AddAudioLoopTrack(string path, string name)
    {
        AddTrack(new TrackModel { Name = ShortName(name), SampleRef = path, Kind = TrackKind.AudioLoop, Volume = 0.9 });
        RefreshLibrary();
    }

    private void AddTrack(TrackModel model)
    {
        _project.Tracks.Add(model);
        var vm = new TrackViewModel(this, model);
        vm.RebuildSteps(_project.GetGrid());
        Tracks.Add(vm);
        SelectedTrack = vm;
        MarkChanged(true);
        _ = LoadSamplesAsync();
    }

    public void RemoveTrack(TrackViewModel track)
    {
        if (!Dialogs.Confirm("Remove track", $"Remove the track '{track.Name}'?"))
        {
            return;
        }

        _project.Tracks.Remove(track.Model);
        Tracks.Remove(track);
        if (SelectedTrack == track)
        {
            SelectedTrack = Tracks.FirstOrDefault();
        }

        MarkChanged(true);
    }

    private void AssignSample()
    {
        if (SelectedTrack is null || SelectedSample is null)
        {
            return;
        }

        SelectedTrack.SetSample(SelectedSample.SampleRef);
        MarkChanged(true);
        _ = LoadSamplesAsync();
    }

    public void TriggerTrack(TrackViewModel track)
    {
        _s.Engine.TriggerTrack(track.Model.Id, 1f);
        track.Level = 1;
        if (RecordPads && IsPlaying && track.IsPattern)
        {
            var total = _project.GetGrid().TotalSteps;
            var step = (int)Math.Round(_s.Engine.CurrentStepExact, MidpointRounding.AwayFromZero) % total;
            track.SetStep(step, true);
        }
    }

    public void TriggerTrackByIndex(int index)
    {
        if (index >= 0 && index < Tracks.Count)
        {
            TriggerTrack(Tracks[index]);
        }
    }

    private static string ShortName(string name)
    {
        var n = name.Contains('/') ? name[(name.LastIndexOf('/') + 1)..].Trim() : name;
        return n.Length > 28 ? n[..28] : n;
    }

    // ================================================================= transport

    public void Play()
    {
        RebuildArrangement();
        _s.Engine.Start();
    }

    public void Stop() => _s.Engine.Stop(SelectedStopMode.Value);

    public void TogglePlay()
    {
        if (_s.Engine.Transport == EngineTransport.Stopped)
        {
            Play();
        }
        else
        {
            Stop();
        }
    }

    private void Panic()
    {
        _s.Engine.Stop(StopMode.Immediate);
        _s.Engine.Panic();
    }

    private void NudgeBpm(object? parameter)
    {
        if (parameter is string s && double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var delta))
        {
            Bpm = Math.Round(Bpm + delta, 2);
        }
    }

    private void TapTempo()
    {
        var now = _tapClock.Elapsed.TotalSeconds;
        if (_taps.Count > 0 && now - _taps[^1] > 2.5)
        {
            _taps.Clear();
        }

        _taps.Add(now);
        if (_taps.Count > 8)
        {
            _taps.RemoveAt(0);
        }

        if (TempoMath.TapTempo(_taps) is double bpm)
        {
            Bpm = Math.Round(bpm);
        }
    }

    /// <summary>Called ~30 times per second by the UI timer. Only reads values published by the audio thread.</summary>
    public void Tick()
    {
        var transport = _s.Engine.Transport;
        if (transport != _transport || transport == EngineTransport.CountIn)
        {
            _transport = transport;
            OnPropertyChanged(nameof(IsPlaying));
            OnPropertyChanged(nameof(IsCountingIn));
            OnPropertyChanged(nameof(IsStopped));
            OnPropertyChanged(nameof(TransportText));
        }

        var step = _s.Engine.CurrentStep;
        if (step != _lastStep)
        {
            foreach (var track in Tracks)
            {
                track.SetCurrentStep(_lastStep, step);
            }

            _lastStep = step;
            var grid = _project.GetGrid();
            var beat = step >= 0 ? step % grid.StepsPerBar / grid.StepsPerBeat : -1;
            for (var i = 0; i < BeatLeds.Count; i++)
            {
                BeatLeds[i].IsActive = i == beat;
            }

            PositionText = step >= 0
                ? $"Bar {step / grid.StepsPerBar + 1}   Beat {beat + 1}"
                : transport == EngineTransport.CountIn ? "Count-in" : "Stopped";
        }

        if (transport == EngineTransport.CountIn)
        {
            var remaining = _s.Engine.CountInBeatsRemaining;
            var beatsPerBar = Math.Max(1, BeatLeds.Count);
            var totalBeats = Math.Max(1, _project.Metronome.CountInBars) * beatsPerBar;
            var beatIndex = Math.Max(0, totalBeats - remaining) % beatsPerBar;
            for (var i = 0; i < BeatLeds.Count; i++)
            {
                BeatLeds[i].IsActive = i == beatIndex;
            }
        }

        var (l, r) = _s.Engine.ReadAndResetPeaks();
        MasterLevelL = Math.Max(l, MasterLevelL * 0.85);
        MasterLevelR = Math.Max(r, MasterLevelR * 0.85);
        foreach (var track in Tracks)
        {
            if (track.Level > 0.01)
            {
                track.Level *= 0.8;
            }
            else if (track.Level > 0)
            {
                track.Level = 0;
            }
        }
    }

    // ================================================================= library

    public void RefreshLibrary()
    {
        _allSamples = _s.Library.Scan();
        var current = SelectedCategory;
        Categories.Clear();
        foreach (var c in SampleLibrary.Categories(_allSamples))
        {
            Categories.Add(c);
        }

        _selectedCategory = Categories.Contains(current) ? current : SampleLibrary.AllCategory;
        OnPropertyChanged(nameof(SelectedCategory));
        FilterSamples();
    }

    private void FilterSamples()
    {
        var query = _allSamples.AsEnumerable();
        if (SelectedCategory != SampleLibrary.AllCategory)
        {
            query = query.Where(s => s.Category == SelectedCategory);
        }

        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            query = query.Where(s => s.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        var selected = SelectedSample;
        VisibleSamples.Clear();
        foreach (var s in query)
        {
            VisibleSamples.Add(s);
        }

        SelectedSample = selected is not null && VisibleSamples.Contains(selected) ? selected : VisibleSamples.FirstOrDefault();
    }

    private async Task PreviewAsync(SampleEntry? entry)
    {
        if (entry is null)
        {
            return;
        }

        try
        {
            var sample = await _s.Samples.GetAsync(entry.FullPath);
            _s.Engine.Preview(sample, (float)PreviewVolume);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report($"'{entry.Name}' cannot be played", ex);
        }
    }

    private async Task ImportSamplesAsync()
    {
        var files = Dialogs.OpenFiles("Import samples", SampleLibrary.FileDialogFilter);
        if (files.Length == 0)
        {
            return;
        }

        var imported = 0;
        var failed = new List<string>();
        foreach (var file in files)
        {
            try
            {
                await Task.Run(() => SampleLoader.Load(file));
                var target = UniqueFile(_s.Paths.ImportedSamples, Path.GetFileName(file));
                File.Copy(file, target);
                imported++;
            }
            catch (Exception ex)
            {
                failed.Add(Path.GetFileName(file));
                _s.Log.Warn($"Import failed: {file}", ex);
            }
        }

        _selectedCategory = SampleLibrary.ImportedCategory;
        RefreshLibrary();
        if (failed.Count > 0)
        {
            ErrorReporter.Report($"Imported {imported} file(s). These could not be read as audio: {string.Join(", ", failed)}", null);
        }
    }

    internal static string UniqueFile(string folder, string fileName)
    {
        Directory.CreateDirectory(folder);
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var path = Path.Combine(folder, fileName);
        var i = 2;
        while (File.Exists(path))
        {
            path = Path.Combine(folder, $"{name} ({i++}){ext}");
        }

        return path;
    }
}
