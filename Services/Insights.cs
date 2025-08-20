using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Saki_ML.Contracts;

namespace Saki_ML.Services
{
    public interface IInsightsBuffer
    {
        void RecordUnsure(string text, ClassificationResult result);
        IReadOnlyList<UnsureEntry> GetRecent(int take = 50, TimeSpan? period = null);
    }

    public sealed class InMemoryInsightsBuffer : IInsightsBuffer
    {
        private readonly ConcurrentQueue<UnsureEntry> _entries = new();
        private readonly int _capacity;

        public InMemoryInsightsBuffer(int capacity = 500)
        {
            _capacity = capacity;
        }

        public void RecordUnsure(string text, ClassificationResult result)
        {
            var entry = new UnsureEntry
            {
                Timestamp = DateTimeOffset.UtcNow,
                Text = Truncate(text, 200),
                PredictedLabel = result.PredictedLabel,
                Confidence = result.Confidence,
                TopScores = result.Scores.Take(3).ToArray(),
                Color = result.Color,
                DurationMs = result.DurationMs
            };
            _entries.Enqueue(entry);
            while (_entries.Count > _capacity && _entries.TryDequeue(out _)) { }
        }

        public IReadOnlyList<UnsureEntry> GetRecent(int take = 50, TimeSpan? period = null)
        {
            var items = _entries.AsEnumerable();
            if (period.HasValue)
            {
                var cutoff = DateTimeOffset.UtcNow - period.Value;
                items = items.Where(e => e.Timestamp >= cutoff);
            }
            return items.Reverse().Take(Math.Max(1, take)).ToList();
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value ?? string.Empty;
            return value.Substring(0, max) + "…";
        }
    }

    public sealed class UnsureEntry
    {
        public DateTimeOffset Timestamp { get; set; }
        public string Text { get; set; } = string.Empty;
        public string PredictedLabel { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public Contracts.LabelScore[] TopScores { get; set; } = Array.Empty<Contracts.LabelScore>();
        public string Color { get; set; } = "#F59E0B";
        public double DurationMs { get; set; }
    }
}


