namespace NetworkAdmin.Server.UI;

internal sealed record TelemetryPoint(DateTime Time, float Cpu, float Ram);

// Presentation-only cache. Polls must not duplicate the same received sample.
internal sealed class TelemetryHistory
{
    private readonly Dictionary<string, Queue<TelemetryPoint>> _samples = new();
    public void Observe(IReadOnlyList<ClientSnapshot> clients)
    {
        var ids = clients.Select(c => c.SessionId).ToHashSet();
        foreach (var id in _samples.Keys.Where(id => !ids.Contains(id)).ToArray()) _samples.Remove(id);
        foreach (var c in clients)
        {
            if (c.LastTelemetryAt is not DateTime at || c.Cpu is not float cpu || c.Ram is not float ram) continue;
            if (!_samples.TryGetValue(c.SessionId, out var points)) _samples[c.SessionId] = points = new();
            if (points.Count > 0 && points.Last().Time >= at) continue;
            points.Enqueue(new(at, cpu, ram));
            while (points.Count > 60) points.Dequeue();
        }
    }
    public IReadOnlyList<TelemetryPoint> For(string? id) => id is not null && _samples.TryGetValue(id, out var p) ? p.ToArray() : [];
}

