using System;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using Saki_ML.Contracts;
using Microsoft.Extensions.Configuration;

namespace Saki_ML.Services
{
    public interface IClassificationService
    {
        ClassificationResult Classify(string text);
    }

    public sealed class MlNetClassificationService : IClassificationService
    {
        private readonly double _unsureThreshold;
        private readonly double _blockThreshold;

        public MlNetClassificationService(IConfiguration configuration)
        {
            // Defaults
            _unsureThreshold = 0.75d;
            _blockThreshold = 0.85d;

            if (double.TryParse(configuration["UnsureThreshold"], NumberStyles.Float, CultureInfo.InvariantCulture, out var ut) && ut > 0 && ut < 1)
            {
                _unsureThreshold = ut;
            }
            if (double.TryParse(configuration["BlockThreshold"], NumberStyles.Float, CultureInfo.InvariantCulture, out var bt) && bt > 0 && bt < 1)
            {
                _blockThreshold = bt;
            }
        }

        public ClassificationResult Classify(string text)
        {
            var stopwatch = Stopwatch.StartNew();
            var input = new SpamClassifier.ModelInput { Label = string.Empty, Text = text };
            var output = SpamClassifier.Predict(input);
            var scores = SpamClassifier.GetSortedScoresWithLabels(output)
                .Select(kv => new LabelScore(kv.Key, ClampScoreDecimal(kv.Value)))
                .ToArray();
            var top = scores.First();

            var result = new ClassificationResult
            {
                PredictedLabel = output.PredictedLabel,
                Confidence = top.Score,
                Scores = scores
            };

            stopwatch.Stop();
            EnrichVerdict(result);
            result.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
            return result;
        }

        private void EnrichVerdict(ClassificationResult result)
        {
            var conf = (double)result.Confidence;
            if (conf < _unsureThreshold)
            {
                result.Verdict = ClassificationVerdict.Unsure;
                result.Blocked = false;
                result.Color = "#F59E0B"; // amber
                result.Explanation = "Low confidence prediction; action requires manual policy or higher threshold.";
                return;
            }

            var isSpam = string.Equals(result.PredictedLabel, "spam", StringComparison.OrdinalIgnoreCase);
            if (isSpam && conf < _blockThreshold)
            {
                result.Verdict = ClassificationVerdict.Unsure;
                result.Blocked = false;
                result.Color = "#F59E0B";
                result.Explanation = "Spam predicted but below block threshold; recommends manual review or adjust BlockThreshold.";
                return;
            }

            result.Verdict = isSpam ? ClassificationVerdict.Block : ClassificationVerdict.Allow;
            result.Blocked = isSpam;
            result.Color = isSpam ? "#DC2626" : "#16A34A"; // red or green
            result.Explanation = isSpam ? "High confidence spam; message should be blocked." : "High confidence ham; message should be allowed.";
        }

        private static decimal ClampScoreDecimal(float score)
        {
            var clamped = Math.Clamp(score, 0f, 1f);
            return Math.Round((decimal)clamped, 7, MidpointRounding.AwayFromZero);
        }
    }
}


