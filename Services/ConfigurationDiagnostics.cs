using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace Saki_ML.Services
{
    public interface IConfigurationDiagnostics
    {
        ConfigDiagnosticsResult Inspect();
    }

    public sealed class ConfigurationDiagnostics : IConfigurationDiagnostics
    {
        private readonly IConfiguration _configuration;

        public ConfigurationDiagnostics(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public ConfigDiagnosticsResult Inspect()
        {
            var results = new List<ConfigIssue>();

            var apiKey = _configuration["ApiKey"] ?? Environment.GetEnvironmentVariable("SAKI_ML_API_KEY");
            if (string.IsNullOrWhiteSpace(apiKey) || apiKey == "dev-key")
            {
                results.Add(new ConfigIssue
                {
                    Key = "SAKI_ML_API_KEY",
                    Severity = IssueSeverity.Warning,
                    Message = string.IsNullOrWhiteSpace(apiKey)
                        ? "API key is not set. Set environment variable SAKI_ML_API_KEY."
                        : "Using development key. Set a strong value for SAKI_ML_API_KEY."
                });
            }

            if (!int.TryParse(_configuration["QueueCapacity"], out var queueCap) || queueCap <= 0)
            {
                results.Add(new ConfigIssue
                {
                    Key = "QueueCapacity",
                    Severity = IssueSeverity.Info,
                    Message = "QueueCapacity is not set or invalid. Falling back to default 1000."
                });
            }

            var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
            if (string.IsNullOrWhiteSpace(urls))
            {
                results.Add(new ConfigIssue
                {
                    Key = "ASPNETCORE_URLS",
                    Severity = IssueSeverity.Info,
                    Message = "ASPNETCORE_URLS not set. Default is http://0.0.0.0:8080"
                });
            }

            return new ConfigDiagnosticsResult
            {
                Issues = results.ToArray()
            };
        }
    }

    public sealed class ConfigDiagnosticsResult
    {
        public ConfigIssue[] Issues { get; set; } = Array.Empty<ConfigIssue>();
    }

    public sealed class ConfigIssue
    {
        public string Key { get; set; } = string.Empty;
        public IssueSeverity Severity { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public enum IssueSeverity
    {
        Info,
        Warning,
        Error
    }
}


