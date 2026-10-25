using System.Collections.Concurrent;

namespace WorshipLoopStudio.Audio.IO;

/// <summary>Keeps decoded samples in memory so the audio thread never touches the disk.</summary>
public sealed class SampleCache
{
    private readonly ConcurrentDictionary<string, Lazy<Task<CachedSample>>> _entries = new(StringComparer.OrdinalIgnoreCase);

    public Task<CachedSample> GetAsync(string path)
    {
        var key = Path.GetFullPath(path);
        var lazy = _entries.GetOrAdd(key, k => new Lazy<Task<CachedSample>>(() => Task.Run(() => SampleLoader.Load(k))));
        var task = lazy.Value;
        task.ContinueWith(t => _entries.TryRemove(new KeyValuePair<string, Lazy<Task<CachedSample>>>(key, lazy)),
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        return task;
    }

    public bool TryGetLoaded(string path, out CachedSample sample)
    {
        sample = CachedSample.Empty;
        if (_entries.TryGetValue(Path.GetFullPath(path), out var lazy) && lazy.IsValueCreated && lazy.Value.IsCompletedSuccessfully)
        {
            sample = lazy.Value.Result;
            return true;
        }

        return false;
    }

    public void Invalidate(string path) => _entries.TryRemove(Path.GetFullPath(path), out _);
}
