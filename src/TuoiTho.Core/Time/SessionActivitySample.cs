namespace TuoiTho.Core.Time;

/// <summary>Minimal activity metadata observed inside the managed interactive Windows session.</summary>
public sealed record SessionActivitySample(string ProfileId, int SessionId, DateTimeOffset ObservedAtUtc, double IdleSeconds);