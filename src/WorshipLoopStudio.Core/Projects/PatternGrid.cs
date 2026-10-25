namespace WorshipLoopStudio.Core.Projects;

public readonly record struct PatternGrid(int BeatsPerBar, int StepsPerBeat, int Bars)
{
    public int StepsPerBar => BeatsPerBar * StepsPerBeat;
    public int TotalSteps => StepsPerBar * Bars;
}

/// <summary>Moves pattern steps to a new grid when the time signature, quantization or bar count changes.</summary>
public static class PatternRemapper
{
    public static List<StepEvent> Remap(IEnumerable<StepEvent> steps, PatternGrid from, PatternGrid to, bool repeatToFill)
    {
        var result = new Dictionary<int, float>();
        var source = steps.Where(s => s.Index >= 0 && s.Index < from.TotalSteps).ToList();

        void Put(int index, float velocity)
        {
            if (index < 0 || index >= to.TotalSteps)
            {
                return;
            }

            result[index] = result.TryGetValue(index, out var existing) ? Math.Max(existing, velocity) : velocity;
        }

        int? MapInBar(int inBarIndex)
        {
            var beat = inBarIndex / from.StepsPerBeat;
            var sub = inBarIndex % from.StepsPerBeat;
            var beatPosition = beat + sub / (double)from.StepsPerBeat;
            var mapped = (int)Math.Round(beatPosition * to.StepsPerBeat, MidpointRounding.AwayFromZero);
            return mapped < to.StepsPerBar ? mapped : null;
        }

        foreach (var step in source)
        {
            var bar = step.Index / from.StepsPerBar;
            var mapped = MapInBar(step.Index % from.StepsPerBar);
            if (mapped is null || bar >= to.Bars)
            {
                continue;
            }

            Put(bar * to.StepsPerBar + mapped.Value, step.Velocity);
        }

        if (repeatToFill && to.Bars > from.Bars && from.Bars > 0)
        {
            foreach (var step in source)
            {
                var bar = step.Index / from.StepsPerBar;
                var mapped = MapInBar(step.Index % from.StepsPerBar);
                if (mapped is null)
                {
                    continue;
                }

                for (var targetBar = from.Bars + bar; targetBar < to.Bars; targetBar += from.Bars)
                {
                    Put(targetBar * to.StepsPerBar + mapped.Value, step.Velocity);
                }
            }
        }

        return result.OrderBy(kv => kv.Key).Select(kv => new StepEvent { Index = kv.Key, Velocity = kv.Value }).ToList();
    }
}
