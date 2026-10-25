using System.Collections.Concurrent;
using NAudio.Wave;
using WorshipLoopStudio.Audio.IO;
using WorshipLoopStudio.Core.Timing;

namespace WorshipLoopStudio.Audio.Engine;

/// <summary>
/// Sample-accurate loop sequencer. All tracks share one musical clock (the loop position in frames).
/// Real-time rules: Read() never allocates, never locks, never touches the disk and never calls the UI.
/// The UI communicates through an immutable arrangement snapshot and a lock-free command queue.
/// </summary>
public sealed class LoopEngine : ISampleProvider
{
    public const int SampleRate = EngineFormat.SampleRate;
    private const int MaxVoices = 96;
    private const int FadeFrames = 256;

    private readonly Voice[] _voices = new Voice[MaxVoices];
    private readonly ConcurrentQueue<EngineCommand> _commands = new();
    private readonly float[] _clickAccent;
    private readonly float[] _clickNormal;

    private EngineArrangement _pending = EngineArrangement.Empty;
    private EngineArrangement _active = EngineArrangement.Empty;
    private double _framesPerStep;
    private double _framesPerBeat;
    private long _loopLength;
    private long _loopPos;
    private int _nextStep;
    private EngineTransport _transport = EngineTransport.Stopped;
    private StopMode? _pendingStop;
    private long _countInPos;
    private int _countInNextBeat;
    private int _countInTotalBeats;
    private long _voiceCounter;
    private long _loopCount;

    // Values published for the UI (read with Volatile from another thread).
    private int _reportedStep = -1;
    private int _reportedTransport;
    private int _reportedCountInBeatsLeft;
    private long _reportedLoopPos;
    private double _reportedFramesPerStep = 1;
    private long _reportedLoopCount;
    private float _peakL;
    private float _peakR;

    public LoopEngine()
    {
        for (var i = 0; i < _voices.Length; i++)
        {
            _voices[i] = new Voice();
        }

        _clickAccent = MakeClick(1600, 0.03);
        _clickNormal = MakeClick(1100, 0.025);
        ApplyArrangement(EngineArrangement.Empty);
    }

    public WaveFormat WaveFormat { get; } = EngineFormat.WaveFormat;

    public EngineTransport Transport => (EngineTransport)Volatile.Read(ref _reportedTransport);

    /// <summary>Index of the step currently sounding, or -1 when stopped / counting in.</summary>
    public int CurrentStep => Volatile.Read(ref _reportedStep);

    public int CountInBeatsRemaining => Volatile.Read(ref _reportedCountInBeatsLeft);

    public long LoopCount => Volatile.Read(ref _reportedLoopCount);

    /// <summary>Fractional step position (for recording pad hits into the pattern).</summary>
    public double CurrentStepExact
    {
        get
        {
            var pos = Volatile.Read(ref _reportedLoopPos);
            var fps = Volatile.Read(ref _reportedFramesPerStep);
            return fps > 0 ? pos / fps : 0;
        }
    }

    public void SetArrangement(EngineArrangement arrangement)
    {
        ArgumentNullException.ThrowIfNull(arrangement);
        Volatile.Write(ref _pending, arrangement);
    }

    public void Start() => _commands.Enqueue(new EngineCommand { Kind = EngineCommandKind.Start });

    public void Stop(StopMode mode) => _commands.Enqueue(new EngineCommand { Kind = EngineCommandKind.Stop, StopMode = mode });

    public void Preview(CachedSample sample, float volume = 0.8f) =>
        _commands.Enqueue(new EngineCommand { Kind = EngineCommandKind.Preview, Sample = sample, Value = volume });

    public void StopPreview() => _commands.Enqueue(new EngineCommand { Kind = EngineCommandKind.StopPreview });

    public void TriggerTrack(string trackId, float velocity = 1f) =>
        _commands.Enqueue(new EngineCommand { Kind = EngineCommandKind.TriggerTrack, TrackId = trackId, Value = velocity });

    /// <summary>Silences everything immediately (fades over a few milliseconds to avoid a click).</summary>
    public void Panic() => _commands.Enqueue(new EngineCommand { Kind = EngineCommandKind.Panic });

    public (float Left, float Right) ReadAndResetPeaks() =>
        (Interlocked.Exchange(ref _peakL, 0f), Interlocked.Exchange(ref _peakR, 0f));

    public int Read(float[] buffer, int offset, int count)
    {
        var frames = count / 2;
        Array.Clear(buffer, offset, frames * 2);

        var pending = Volatile.Read(ref _pending);
        if (!ReferenceEquals(pending, _active))
        {
            ApplyArrangement(pending);
        }

        ProcessCommands();
        UpdateTrackTargets();

        var i = 0;
        var guard = 0;
        while (i < frames)
        {
            long untilEvent = _transport switch
            {
                EngineTransport.CountIn => CountInBeatStart(_countInNextBeat) - _countInPos,
                EngineTransport.Playing => PlayEventPosition() - _loopPos,
                _ => long.MaxValue,
            };

            if (untilEvent < 0)
            {
                untilEvent = 0;
            }

            var chunk = (int)Math.Min(untilEvent, frames - i);
            if (chunk > 0)
            {
                RenderVoices(buffer, offset, i, chunk, frames);
                if (_transport == EngineTransport.Playing)
                {
                    _loopPos += chunk;
                }
                else if (_transport == EngineTransport.CountIn)
                {
                    _countInPos += chunk;
                }

                i += chunk;
            }

            if (_transport != EngineTransport.Stopped && untilEvent == chunk)
            {
                if (_transport == EngineTransport.CountIn)
                {
                    HandleCountInEvent();
                }
                else
                {
                    HandlePlayEvent();
                }

                // Safety net: an event must always move time forward. This can only trip on a logic error.
                if (++guard > 100_000)
                {
                    StopTransport();
                }
            }
        }

        FinishTrackGains();
        ApplyMasterAndMeasure(buffer, offset, frames);
        Publish();
        return frames * 2;
    }

    // ------------------------------------------------------------------ timeline

    private long StepStart(int step) => TempoMath.StepStartFrame(step, _framesPerStep);

    private long PlayEventPosition() => _nextStep >= _active.TotalSteps ? _loopLength : StepStart(_nextStep);

    private long CountInBeatStart(int beat) => (long)Math.Round(beat * _framesPerBeat, MidpointRounding.AwayFromZero);

    private void HandleCountInEvent()
    {
        if (_countInNextBeat >= _countInTotalBeats)
        {
            BeginPlaying();
            return;
        }

        var accent = _countInNextBeat % _active.BeatsPerBar == 0;
        TriggerClick(accent, Math.Max(_active.MetronomeVolume, 0.25f));
        _countInNextBeat++;
    }

    private void HandlePlayEvent()
    {
        var total = _active.TotalSteps;
        if (_nextStep >= total)
        {
            // End of loop: wrap seamlessly. Step 0 of the next cycle is triggered at the same frame.
            _loopPos = 0;
            _nextStep = 0;
            _loopCount++;
            if (_pendingStop is StopMode.EndOfLoop or StopMode.EndOfBar)
            {
                StopTransport();
            }

            return;
        }

        var step = _nextStep;
        if (_pendingStop == StopMode.EndOfBar && step % _active.StepsPerBar == 0 && step != 0)
        {
            StopTransport();
            return;
        }

        TriggerStep(step);
        _nextStep++;
    }

    private void TriggerStep(int step)
    {
        var arrangement = _active;
        if (arrangement.MetronomeEnabled && step % arrangement.StepsPerBeat == 0)
        {
            TriggerClick(step % arrangement.StepsPerBar == 0, arrangement.MetronomeVolume);
        }

        foreach (var track in arrangement.Tracks)
        {
            if (track.Sample.IsEmpty)
            {
                continue;
            }

            if (track.IsAudioLoop)
            {
                if (step == 0)
                {
                    ChokeTrack(track.Id);
                    StartVoice(track.Sample.Data, track, 1f, 0, 0, false);
                }

                continue;
            }

            if (step < track.StepVelocities.Length)
            {
                var velocity = track.StepVelocities[step];
                if (velocity > 0)
                {
                    StartVoice(track.Sample.Data, track, velocity, 0, 0, false);
                }
            }
        }
    }

    private void BeginPlaying()
    {
        _transport = EngineTransport.Playing;
        _loopPos = 0;
        _nextStep = 0;
    }

    private void StopTransport()
    {
        _transport = EngineTransport.Stopped;
        _pendingStop = null;
        _loopPos = 0;
        _nextStep = 0;
        _countInPos = 0;
        _countInNextBeat = 0;
    }

    // ------------------------------------------------------------------ commands

    private void ProcessCommands()
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command.Kind)
            {
                case EngineCommandKind.Start:
                    if (_transport == EngineTransport.Stopped)
                    {
                        _pendingStop = null;
                        _loopCount = 0;
                        if (_active.CountInBars > 0)
                        {
                            _transport = EngineTransport.CountIn;
                            _countInPos = 0;
                            _countInNextBeat = 0;
                            _countInTotalBeats = _active.CountInBars * _active.BeatsPerBar;
                        }
                        else
                        {
                            BeginPlaying();
                        }
                    }
                    else
                    {
                        // Pressing play again while a stop is queued cancels the stop.
                        _pendingStop = null;
                    }

                    break;

                case EngineCommandKind.Stop:
                    if (_transport == EngineTransport.CountIn)
                    {
                        StopTransport();
                    }
                    else if (_transport == EngineTransport.Playing)
                    {
                        switch (command.StopMode)
                        {
                            case StopMode.Immediate:
                                StopTransport();
                                FadeAllVoices();
                                break;
                            case StopMode.Release:
                                StopTransport();
                                break;
                            default:
                                _pendingStop = command.StopMode;
                                break;
                        }
                    }
                    else if (command.StopMode == StopMode.Immediate)
                    {
                        FadeAllVoices();
                    }

                    break;

                case EngineCommandKind.Preview:
                    if (command.Sample is { IsEmpty: false } sample)
                    {
                        FadePreviewVoices();
                        StartVoice(sample.Data, null, 1f, command.Value, command.Value, true);
                    }

                    break;

                case EngineCommandKind.StopPreview:
                    FadePreviewVoices();
                    break;

                case EngineCommandKind.TriggerTrack:
                    foreach (var track in _active.Tracks)
                    {
                        if (track.Id == command.TrackId && !track.Sample.IsEmpty)
                        {
                            if (track.IsAudioLoop)
                            {
                                ChokeTrack(track.Id);
                            }

                            StartVoice(track.Sample.Data, track, command.Value, 0, 0, false);
                            break;
                        }
                    }

                    break;

                case EngineCommandKind.Panic:
                    FadeAllVoices();
                    break;
            }
        }
    }

    // ------------------------------------------------------------------ arrangement

    private void ApplyArrangement(EngineArrangement next)
    {
        var previous = _active;
        var oldFramesPerBeat = _framesPerBeat;
        _active = next;
        _framesPerStep = TempoMath.FramesPerStep(next.Bpm, SampleRate, next.StepsPerBeat);
        _framesPerBeat = TempoMath.FramesPerBeat(next.Bpm, SampleRate);
        _loopLength = TempoMath.LoopLengthFrames(next.TotalSteps, _framesPerStep);

        if (_transport == EngineTransport.Playing && oldFramesPerBeat > 0 && !ReferenceEquals(previous, next))
        {
            // Keep the musical position (in beats) when the tempo or grid changes, without retriggering steps.
            var beatPosition = _loopPos / oldFramesPerBeat;
            var nextStepBeat = _nextStep / (double)Math.Max(1, previous.StepsPerBeat);
            var newNext = (int)Math.Ceiling(nextStepBeat * next.StepsPerBeat - 1e-9);
            var newPos = (long)Math.Floor(beatPosition * _framesPerBeat);
            if (newNext >= next.TotalSteps || newPos >= _loopLength)
            {
                _nextStep = next.TotalSteps;
                _loopPos = Math.Min(newPos, _loopLength);
            }
            else
            {
                _nextStep = Math.Max(0, newNext);
                _loopPos = Math.Min(newPos, StepStart(_nextStep));
            }
        }
        else if (_transport == EngineTransport.CountIn && oldFramesPerBeat > 0)
        {
            var beatPosition = _countInPos / oldFramesPerBeat;
            _countInTotalBeats = Math.Max(_countInNextBeat, next.CountInBars * next.BeatsPerBar);
            _countInPos = Math.Min((long)Math.Floor(beatPosition * _framesPerBeat), CountInBeatStart(_countInNextBeat));
        }

        // Carry gain smoothing state across snapshots and re-point ringing voices at the new track objects.
        foreach (var track in next.Tracks)
        {
            foreach (var old in previous.Tracks)
            {
                if (old.Id == track.Id && old.GainInitialized)
                {
                    track.GainL = old.GainL;
                    track.GainR = old.GainR;
                    track.GainInitialized = true;
                    break;
                }
            }
        }

        foreach (var voice in _voices)
        {
            if (!voice.Active || voice.Track is null)
            {
                continue;
            }

            EngineTrack? replacement = null;
            foreach (var track in next.Tracks)
            {
                if (track.Id == voice.Track.Id)
                {
                    replacement = track;
                    break;
                }
            }

            if (replacement is null)
            {
                voice.FixedGainL = voice.Track.GainL;
                voice.FixedGainR = voice.Track.GainR;
                voice.Track = null;
                if (voice.FadeRemaining < 0)
                {
                    voice.FadeRemaining = FadeFrames;
                }
            }
            else
            {
                voice.Track = replacement;
            }
        }
    }

    private void UpdateTrackTargets()
    {
        var anySolo = false;
        foreach (var track in _active.Tracks)
        {
            if (track.Solo)
            {
                anySolo = true;
                break;
            }
        }

        foreach (var track in _active.Tracks)
        {
            var audible = !track.Mute && (!anySolo || track.Solo);
            var volume = audible ? Math.Clamp(track.Volume, 0f, 1.5f) : 0f;
            var angle = (Math.Clamp(track.Pan, -1f, 1f) + 1f) * (MathF.PI / 4f);
            track.TargetL = volume * MathF.Cos(angle) * MathF.Sqrt(2f);
            track.TargetR = volume * MathF.Sin(angle) * MathF.Sqrt(2f);
            if (!track.GainInitialized)
            {
                track.GainL = track.TargetL;
                track.GainR = track.TargetR;
                track.GainInitialized = true;
            }
        }
    }

    private void FinishTrackGains()
    {
        foreach (var track in _active.Tracks)
        {
            track.GainL = track.TargetL;
            track.GainR = track.TargetR;
        }
    }

    // ------------------------------------------------------------------ voices

    private void StartVoice(float[] data, EngineTrack? track, float velocity, float fixedL, float fixedR, bool isPreview)
    {
        if (data.Length < 2)
        {
            return;
        }

        Voice? target = null;
        Voice oldest = _voices[0];
        foreach (var voice in _voices)
        {
            if (!voice.Active)
            {
                target = voice;
                break;
            }

            if (voice.Age < oldest.Age)
            {
                oldest = voice;
            }
        }

        target ??= oldest;
        target.Active = true;
        target.Data = data;
        target.Frames = data.Length / 2;
        target.Position = 0;
        target.Velocity = velocity;
        target.Track = track;
        target.FixedGainL = fixedL;
        target.FixedGainR = fixedR;
        target.FadeRemaining = -1;
        target.Age = ++_voiceCounter;
        target.IsPreview = isPreview;
    }

    private void TriggerClick(bool accent, float volume)
    {
        if (volume <= 0)
        {
            return;
        }

        StartVoice(accent ? _clickAccent : _clickNormal, null, 1f, volume, volume, false);
    }

    private void ChokeTrack(string trackId)
    {
        foreach (var voice in _voices)
        {
            if (voice.Active && voice.Track is not null && voice.Track.Id == trackId && voice.FadeRemaining < 0)
            {
                voice.FadeRemaining = FadeFrames;
            }
        }
    }

    private void FadeAllVoices()
    {
        foreach (var voice in _voices)
        {
            if (voice.Active && voice.FadeRemaining < 0)
            {
                voice.FadeRemaining = FadeFrames;
            }
        }
    }

    private void FadePreviewVoices()
    {
        foreach (var voice in _voices)
        {
            if (voice.Active && voice.IsPreview && voice.FadeRemaining < 0)
            {
                voice.FadeRemaining = FadeFrames;
            }
        }
    }

    private void RenderVoices(float[] buffer, int offset, int startFrame, int length, int totalFrames)
    {
        var invTotal = 1f / Math.Max(1, totalFrames);
        foreach (var voice in _voices)
        {
            if (!voice.Active)
            {
                continue;
            }

            var data = voice.Data;
            var track = voice.Track;
            var available = voice.Frames - voice.Position;
            var n = Math.Min(length, available);
            var bi = offset + startFrame * 2;
            var si = voice.Position * 2;
            var processed = 0;
            for (var k = 0; k < n; k++)
            {
                var fade = 1f;
                if (voice.FadeRemaining >= 0)
                {
                    if (voice.FadeRemaining == 0)
                    {
                        voice.Active = false;
                        break;
                    }

                    fade = voice.FadeRemaining / (float)FadeFrames;
                    voice.FadeRemaining--;
                }

                float gl, gr;
                if (track is not null)
                {
                    var t = (startFrame + k + 1) * invTotal;
                    gl = track.GainL + (track.TargetL - track.GainL) * t;
                    gr = track.GainR + (track.TargetR - track.GainR) * t;
                }
                else
                {
                    gl = voice.FixedGainL;
                    gr = voice.FixedGainR;
                }

                var g = voice.Velocity * fade;
                buffer[bi] += data[si] * gl * g;
                buffer[bi + 1] += data[si + 1] * gr * g;
                bi += 2;
                si += 2;
                processed++;
            }

            voice.Position += processed;
            if (voice.Position >= voice.Frames)
            {
                voice.Active = false;
            }
        }
    }

    private void ApplyMasterAndMeasure(float[] buffer, int offset, int frames)
    {
        var master = Math.Clamp(_active.MasterVolume, 0f, 1.5f);
        float peakL = 0, peakR = 0;
        for (var f = 0; f < frames; f++)
        {
            var li = offset + f * 2;
            var l = buffer[li] * master;
            var r = buffer[li + 1] * master;
            l = l > 1f ? 1f : l < -1f ? -1f : l;
            r = r > 1f ? 1f : r < -1f ? -1f : r;
            buffer[li] = l;
            buffer[li + 1] = r;
            var al = Math.Abs(l);
            var ar = Math.Abs(r);
            if (al > peakL) peakL = al;
            if (ar > peakR) peakR = ar;
        }

        if (peakL > Volatile.Read(ref _peakL)) Volatile.Write(ref _peakL, peakL);
        if (peakR > Volatile.Read(ref _peakR)) Volatile.Write(ref _peakR, peakR);
    }

    private void Publish()
    {
        var step = _transport == EngineTransport.Playing ? Math.Clamp(_nextStep - 1, 0, Math.Max(0, _active.TotalSteps - 1)) : -1;
        Volatile.Write(ref _reportedStep, step);
        Volatile.Write(ref _reportedTransport, (int)_transport);
        Volatile.Write(ref _reportedCountInBeatsLeft, _transport == EngineTransport.CountIn ? _countInTotalBeats - _countInNextBeat + 1 : 0);
        Volatile.Write(ref _reportedLoopPos, _loopPos);
        Volatile.Write(ref _reportedFramesPerStep, _framesPerStep);
        Volatile.Write(ref _reportedLoopCount, _loopCount);
    }

    private static float[] MakeClick(double frequency, double seconds)
    {
        var frames = (int)(seconds * SampleRate);
        var data = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var t = i / (double)SampleRate;
            var env = Math.Min(1.0, t / 0.001) * Math.Exp(-t / (seconds / 4));
            var v = (float)(Math.Sin(2 * Math.PI * frequency * t) * env * 0.9);
            data[i * 2] = v;
            data[i * 2 + 1] = v;
        }

        return data;
    }
}
