using System;
using System.Collections.Generic;

namespace Saki_ML.Contracts
{
    public enum ClassificationVerdict
    {
        Allow,
        Block,
        Unsure
    }

    public sealed record ClassificationRequestBody(string Text);

    public sealed record LabelScore(string Label, decimal Score);

    public sealed class ClassificationResult
    {
        public string PredictedLabel { get; set; } = string.Empty;
        public decimal Confidence { get; set; }
        public LabelScore[] Scores { get; set; } = Array.Empty<LabelScore>();

        // Enriched fields for insights
        public ClassificationVerdict Verdict { get; set; } = ClassificationVerdict.Unsure;
        public bool Blocked { get; set; }
        public string Color { get; set; } = "#9CA3AF"; // default gray
        public double DurationMs { get; set; }
        public string Explanation { get; set; } = string.Empty;
    }
}


