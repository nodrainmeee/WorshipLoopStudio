using WorshipLoopStudio.Core.Projects;
using WorshipLoopStudio.Core.Timing;

namespace WorshipLoopStudio.Tests;

public class ProjectTests
{
    [Fact]
    public void Serializer_RoundTripsEverything()
    {
        var project = Project.CreateDefault("Sunday");
        project.Bpm = 96.5;
        project.SetTimeSignature(new TimeSignature(6, 8));
        project.Quantization = Quantization.Eighth;
        project.Bars = 4;
        project.Metronome.CountInBars = 2;
        project.Tracks[0].Mute = true;
        project.Tracks[1].Solo = true;
        project.Tracks[1].Pan = -0.5;
        project.Tracks.Add(new TrackModel { Name = "Choir loop", Kind = TrackKind.AudioLoop, SampleRef = @"C:\loops\choir.wav" });
        project.RecordingReferences.Add(@"C:\rec\take1.wav");
        project.Normalize();

        var json = ProjectSerializer.Serialize(project);
        var back = ProjectSerializer.Deserialize(json);

        Assert.Equal("Sunday", back.Name);
        Assert.Equal(96.5, back.Bpm);
        Assert.Equal(new TimeSignature(6, 8), back.GetTimeSignature());
        Assert.Equal(Quantization.Eighth, back.Quantization);
        Assert.Equal(4, back.Bars);
        Assert.Equal(2, back.Metronome.CountInBars);
        Assert.Equal(project.Tracks.Count, back.Tracks.Count);
        Assert.True(back.Tracks[0].Mute);
        Assert.True(back.Tracks[1].Solo);
        Assert.Equal(-0.5, back.Tracks[1].Pan);
        Assert.Equal(TrackKind.AudioLoop, back.Tracks[^1].Kind);
        Assert.Equal(project.Tracks[0].Steps.Select(s => s.Index), back.Tracks[0].Steps.Select(s => s.Index));
        Assert.Single(back.RecordingReferences);
        Assert.Contains("\"AudioLoop\"", json);
    }

    [Fact]
    public void Serializer_RejectsDamagedFiles()
    {
        Assert.Throws<ProjectLoadException>(() => ProjectSerializer.Deserialize("{ not json"));
        Assert.Throws<ProjectLoadException>(() => ProjectSerializer.Deserialize("null"));
    }

    [Fact]
    public void Serializer_RejectsNewerFormat()
    {
        var json = "{\"FormatVersion\": 99, \"Name\": \"x\"}";
        var ex = Assert.Throws<ProjectLoadException>(() => ProjectSerializer.Deserialize(json));
        Assert.Contains("newer version", ex.Message);
    }

    [Fact]
    public void Normalize_ClampsValuesAndDropsInvalidSteps()
    {
        var project = new Project { Bpm = 1000, Bars = 3, TimeSignatureNumerator = 99, MasterVolume = 5 };
        project.Tracks.Add(new TrackModel
        {
            Volume = 9,
            Pan = 4,
            Steps = { new StepEvent { Index = -1 }, new StepEvent { Index = 5 }, new StepEvent { Index = 5, Velocity = 0.5f }, new StepEvent { Index = 999 } },
        });

        project.Normalize();

        Assert.Equal(TempoMath.MaxBpm, project.Bpm);
        Assert.Contains(project.Bars, Project.AllowedBars);
        Assert.Equal(TimeSignature.FourFour, project.GetTimeSignature());
        Assert.Equal(1.5, project.MasterVolume);
        Assert.Equal(1.5, project.Tracks[0].Volume);
        Assert.Equal(1, project.Tracks[0].Pan);
        Assert.Equal(new[] { 5 }, project.Tracks[0].Steps.Select(s => s.Index));
    }

    [Fact]
    public void SaveAndLoad_UsesFilesAtomically()
    {
        var dir = TempDir();
        var path = Path.Combine(dir, "p", "p.wlsproj");
        var project = Project.CreateDefault("p");
        ProjectSerializer.Save(project, path);
        project.Bpm = 70;
        ProjectSerializer.Save(project, path);
        Assert.False(File.Exists(path + ".tmp"));
        Assert.Equal(70, ProjectSerializer.Load(path).Bpm);
        Assert.Throws<ProjectLoadException>(() => ProjectSerializer.Load(Path.Combine(dir, "missing.wlsproj")));
    }

    [Fact]
    public void ProjectStore_RenamesFolderAndFile()
    {
        var store = new ProjectStore(TempDir());
        var path = store.UniquePathFor("Old Name");
        var project = Project.CreateDefault("Old Name");
        ProjectSerializer.Save(project, path);

        var newPath = store.Rename(project, path, "New Name");

        Assert.False(File.Exists(path));
        Assert.True(File.Exists(newPath));
        Assert.EndsWith(Path.Combine("New Name", "New Name.wlsproj"), newPath);
        Assert.Equal("New Name", ProjectSerializer.Load(newPath).Name);
        Assert.Single(store.List());
    }

    [Fact]
    public void ProjectStore_SanitizesNames()
    {
        Assert.Equal("a_b_c", ProjectStore.SanitizeName("a/b:c"));
        Assert.Equal("Untitled", ProjectStore.SanitizeName("   "));
    }

    [Fact]
    public void PatternRemapper_ChangesQuantization()
    {
        var steps = new List<StepEvent> { new() { Index = 0 }, new() { Index = 4 }, new() { Index = 6 } };
        var result = PatternRemapper.Remap(steps, new PatternGrid(4, 4, 1), new PatternGrid(4, 2, 1), false);
        Assert.Equal(new[] { 0, 2, 3 }, result.Select(s => s.Index));
    }

    [Fact]
    public void PatternRemapper_ChangesTimeSignature()
    {
        // 4/4 -> 3/4 (16ths): the 4th beat of each bar does not exist any more.
        var steps = new List<StepEvent> { new() { Index = 13 }, new() { Index = 16 }, new() { Index = 20 } };
        var result = PatternRemapper.Remap(steps, new PatternGrid(4, 4, 2), new PatternGrid(3, 4, 2), false);
        Assert.Equal(new[] { 12, 16 }, result.Select(s => s.Index));
    }

    [Fact]
    public void PatternRemapper_RepeatsWhenAddingBars()
    {
        var steps = new List<StepEvent> { new() { Index = 0 }, new() { Index = 10 } };
        var result = PatternRemapper.Remap(steps, new PatternGrid(4, 4, 1), new PatternGrid(4, 4, 4), true);
        Assert.Equal(new[] { 0, 10, 16, 26, 32, 42, 48, 58 }, result.Select(s => s.Index));
    }

    internal static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "wls-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
