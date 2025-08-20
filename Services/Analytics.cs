using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Saki_ML.Services
{
    public interface IAnalyticsService
    {
        void Record(string verdict, bool blocked, double durationMs);
        AnalyticsSnapshot Snapshot(TimeSpan? period = null);
    }

    public sealed class InMemoryAnalyticsService : IAnalyticsService
    {
        private readonly ConcurrentQueue<TimedEvent> _events = new();

        public void Record(string verdict, bool blocked, double durationMs)
        {
            _events.Enqueue(new TimedEvent
            {
                Timestamp = DateTimeOffset.UtcNow,
                Verdict = verdict,
                Blocked = blocked,
                DurationMs = durationMs
            });

            // Trim queue to keep last N events to bound memory
            while (_events.Count > 100_000 && _events.TryDequeue(out _)) { }
        }

        public AnalyticsSnapshot Snapshot(TimeSpan? period = null)
        {
            var cutoff = period.HasValue ? DateTimeOffset.UtcNow - period.Value : (DateTimeOffset?)null;
            IEnumerable<TimedEvent> source = _events;
            if (cutoff.HasValue)
            {
                source = source.Where(e => e.Timestamp >= cutoff.Value);
            }

            var list = source.ToList();
            var total = list.Count;
            var blocked = list.Count(e => e.Blocked);
            var unsure = list.Count(e => string.Equals(e.Verdict, "Unsure", StringComparison.OrdinalIgnoreCase));
            var avgLatency = list.Count > 0 ? list.Average(e => e.DurationMs) : 0d;

            return new AnalyticsSnapshot
            {
                Total = total,
                Blocked = blocked,
                Unsure = unsure,
                Allowed = total - blocked - unsure,
                AverageLatencyMs = Math.Round(avgLatency, 2)
            };
        }

        private sealed class TimedEvent
        {
            public DateTimeOffset Timestamp { get; set; }
            public string Verdict { get; set; } = string.Empty;
            public bool Blocked { get; set; }
            public double DurationMs { get; set; }
        }
    }

    public sealed class AnalyticsSnapshot
    {
        public int Total { get; set; }
        public int Blocked { get; set; }
        public int Allowed { get; set; }
        public int Unsure { get; set; }
        public double AverageLatencyMs { get; set; }
    }
}


