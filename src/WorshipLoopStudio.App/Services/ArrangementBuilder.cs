using WorshipLoopStudio.Audio.Engine;
using WorshipLoopStudio.Audio.IO;
using WorshipLoopStudio.Core.Projects;
using WorshipLoopStudio.Core.Timing;

namespace WorshipLoopStudio.App.Services;

public static class ArrangementBuilder
{
    /// <summary>Builds the immutable snapshot the audio thread plays. Samples not yet loaded play silence.</summary>
    public static EngineArrangement Build(Project project, Func<TrackModel, CachedSample> sampleFor)
    {
        var grid = project.GetGrid();
        var total = grid.TotalSteps;
        var tracks = new List<EngineTrack>(project.Tracks.Count);
        foreach (var model in project.Tracks)
        {
            var velocities = new float[total];
            foreach (var step in model.Steps)
            {
                if (step.Index >= 0 && step.Index < total)
                {
                    velocities[step.Index] = Math.Clamp(step.Velocity, 0.05f, 1f);
                }
            }

            tracks.Add(new EngineTrack(model.Id, sampleFor(model), velocities, model.Kind == TrackKind.AudioLoop)
            {
                Volume = (float)model.Volume,
                Pan = (float)model.Pan,
                Mute = model.Mute,
                Solo = model.Solo,
            });
        }

        return new EngineArrangement
        {
            Bpm = TempoMath.ClampBpm(project.Bpm),
            BeatsPerBar = grid.BeatsPerBar,
            StepsPerBeat = grid.StepsPerBeat,
            Bars = grid.Bars,
            Tracks = tracks.ToArray(),
            MetronomeEnabled = project.Metronome.Enabled,
            MetronomeVolume = (float)project.Metronome.Volume,
            CountInBars = project.Metronome.CountInBars,
            MasterVolume = (float)project.MasterVolume,
        };
    }
}
