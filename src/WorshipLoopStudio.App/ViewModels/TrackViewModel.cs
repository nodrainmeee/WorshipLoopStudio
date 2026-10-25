using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using WorshipLoopStudio.App.Infrastructure;
using WorshipLoopStudio.Core.Projects;
using WorshipLoopStudio.Core.Samples;

namespace WorshipLoopStudio.App.ViewModels;

public sealed class StepViewModel : ObservableObject
{
    private readonly TrackViewModel _owner;
    private bool _isOn;
    private bool _isCurrent;

    public StepViewModel(TrackViewModel owner, int index, bool isOn, bool isBeatStart, bool isBarStart)
    {
        _owner = owner;
        Index = index;
        _isOn = isOn;
        IsBeatStart = isBeatStart;
        IsBarStart = isBarStart;
    }

    public int Index { get; }
    public bool IsBeatStart { get; }
    public bool IsBarStart { get; }
    public Thickness CellMargin => IsBarStart && Index > 0 ? new Thickness(10, 0, 1, 0) : new Thickness(1, 0, 1, 0);
    public string ToolTipText => $"Step {Index + 1}";

    public bool IsOn
    {
        get => _isOn;
        set
        {
            if (SetProperty(ref _isOn, value))
            {
                _owner.OnStepChanged(this);
            }
        }
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => SetProperty(ref _isCurrent, value);
    }

    internal void SetOnSilently(bool value)
    {
        if (_isOn != value)
        {
            _isOn = value;
            OnPropertyChanged(nameof(IsOn));
        }
    }
}

public sealed class TrackViewModel : ObservableObject
{
    private readonly PerformViewModel _owner;
    private bool _isMissing;
    private string? _problem;
    private bool _isSelected;
    private double _level;

    public TrackViewModel(PerformViewModel owner, TrackModel model)
    {
        _owner = owner;
        Model = model;
        TriggerCommand = new RelayCommand(() => _owner.TriggerTrack(this));
        RemoveCommand = new RelayCommand(() => _owner.RemoveTrack(this));
        ClearCommand = new RelayCommand(Clear);
        SelectCommand = new RelayCommand(() => _owner.SelectedTrack = this);
    }

    public TrackModel Model { get; }
    public ObservableCollection<StepViewModel> Steps { get; } = new();
    public ICommand TriggerCommand { get; }
    public ICommand RemoveCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand SelectCommand { get; }

    public string Name
    {
        get => Model.Name;
        set
        {
            var v = string.IsNullOrWhiteSpace(value) ? "Track" : value.Trim();
            if (Model.Name != v)
            {
                Model.Name = v;
                OnPropertyChanged();
                _owner.MarkChanged(false);
            }
        }
    }

    public string SampleName => SampleRefs.DisplayName(Model.SampleRef);

    public bool IsAudioLoop
    {
        get => Model.Kind == TrackKind.AudioLoop;
        set
        {
            var kind = value ? TrackKind.AudioLoop : TrackKind.Pattern;
            if (Model.Kind != kind)
            {
                Model.Kind = kind;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsPattern));
                _owner.MarkChanged(true);
            }
        }
    }

    public bool IsPattern => !IsAudioLoop;

    public double Volume
    {
        get => Model.Volume;
        set
        {
            var v = Math.Clamp(value, 0, 1.5);
            if (Math.Abs(Model.Volume - v) > 1e-6)
            {
                Model.Volume = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(VolumeText));
                _owner.MarkChanged(true);
            }
        }
    }

    public string VolumeText => Model.Volume <= 0.0001 ? "-inf dB" : $"{20 * Math.Log10(Model.Volume):0.0} dB";

    public double Pan
    {
        get => Model.Pan;
        set
        {
            var v = Math.Clamp(Math.Round(value, 2), -1, 1);
            if (Math.Abs(Model.Pan - v) > 1e-6)
            {
                Model.Pan = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PanText));
                _owner.MarkChanged(true);
            }
        }
    }

    public string PanText => Math.Abs(Model.Pan) < 0.01 ? "C" : Model.Pan < 0 ? $"L{-Model.Pan * 100:0}" : $"R{Model.Pan * 100:0}";

    public bool Mute
    {
        get => Model.Mute;
        set
        {
            if (Model.Mute != value)
            {
                Model.Mute = value;
                OnPropertyChanged();
                _owner.MarkChanged(true);
            }
        }
    }

    public bool Solo
    {
        get => Model.Solo;
        set
        {
            if (Model.Solo != value)
            {
                Model.Solo = value;
                OnPropertyChanged();
                _owner.MarkChanged(true);
            }
        }
    }

    public bool IsMissing
    {
        get => _isMissing;
        set => SetProperty(ref _isMissing, value);
    }

    public string? Problem
    {
        get => _problem;
        set => SetProperty(ref _problem, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    /// <summary>Short visual flash when the pad is hit (0..1).</summary>
    public double Level
    {
        get => _level;
        set => SetProperty(ref _level, value);
    }

    public void SetSample(string sampleRef)
    {
        Model.SampleRef = sampleRef;
        IsMissing = false;
        Problem = null;
        OnPropertyChanged(nameof(SampleName));
    }

    public void RebuildSteps(PatternGrid grid)
    {
        Steps.Clear();
        var on = Model.Steps.Select(s => s.Index).ToHashSet();
        for (var i = 0; i < grid.TotalSteps; i++)
        {
            Steps.Add(new StepViewModel(this, i, on.Contains(i), i % grid.StepsPerBeat == 0, i % grid.StepsPerBar == 0));
        }
    }

    public void SetStep(int index, bool on)
    {
        if (index >= 0 && index < Steps.Count)
        {
            Steps[index].IsOn = on;
        }
    }

    internal void OnStepChanged(StepViewModel step)
    {
        Model.Steps.RemoveAll(s => s.Index == step.Index);
        if (step.IsOn)
        {
            Model.Steps.Add(new StepEvent { Index = step.Index, Velocity = 1f });
            Model.Steps.Sort((a, b) => a.Index.CompareTo(b.Index));
        }

        _owner.MarkChanged(true);
    }

    public void SetCurrentStep(int previous, int current)
    {
        if (previous >= 0 && previous < Steps.Count)
        {
            Steps[previous].IsCurrent = false;
        }

        if (current >= 0 && current < Steps.Count)
        {
            Steps[current].IsCurrent = true;
        }
    }

    private void Clear()
    {
        Model.Steps.Clear();
        foreach (var s in Steps)
        {
            s.SetOnSilently(false);
        }

        _owner.MarkChanged(true);
    }
}
