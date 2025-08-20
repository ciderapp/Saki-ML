using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using Saki_ML;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Saki_ML.Contracts;
using Saki_ML.Services;
using System.Text.Json.Serialization;
using Saki_ML.Utils;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss.fff ";
    options.IncludeScopes = false;
});
// Category filters with env-configurable levels
var appLogLevel = ParseLogLevel(builder.Configuration["LogLevel:App"] ?? Environment.GetEnvironmentVariable("SAKI_ML_LOG_LEVEL_APP")) ?? LogLevel.Information;
var frameworkLogLevel = ParseLogLevel(builder.Configuration["LogLevel:Framework"] ?? Environment.GetEnvironmentVariable("SAKI_ML_LOG_LEVEL_MS")) ?? LogLevel.Warning;
builder.Logging.AddFilter((category, level) =>
{
    if (!string.IsNullOrEmpty(category) &&
        (category.StartsWith("Microsoft", StringComparison.Ordinal) || category.StartsWith("System", StringComparison.Ordinal)))
    {
        return level >= frameworkLogLevel;
    }
    return level >= appLogLevel;
});

// Configuration
var configuration = builder.Configuration;
var apiKey = configuration["ApiKey"] ?? Environment.GetEnvironmentVariable("SAKI_ML_API_KEY") ?? "dev-key";
var queueCapacity = int.TryParse(configuration["QueueCapacity"], out var cap) ? cap : 1000;

// Services
builder.Services.AddLogging();
builder.Services.AddSingleton(Channel.CreateBounded<ClassificationRequest>(new BoundedChannelOptions(queueCapacity)
{
    SingleReader = true,
    SingleWriter = false,
    FullMode = BoundedChannelFullMode.Wait
}));
builder.Services.AddSingleton<IClassificationQueue, ChannelClassificationQueue>();
builder.Services.AddSingleton<IClassificationService, MlNetClassificationService>();
builder.Services.AddSingleton<IAnalyticsService, InMemoryAnalyticsService>();
builder.Services.AddSingleton<IConfigurationDiagnostics, ConfigurationDiagnostics>();
builder.Services.AddSingleton<IInsightsBuffer, InMemoryInsightsBuffer>();
builder.Services.AddHostedService<ClassificationWorker>();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

// Simple API key auth middleware
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/health"))
    {
        await next();
        return;
    }

    if (!context.Request.Headers.TryGetValue("x-api-key", out var provided) || provided != apiKey)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "unauthorized" });
        return;
    }
    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/classify", async (HttpContext http, IClassificationQueue queue, IAnalyticsService analytics, IInsightsBuffer insights, ILoggerFactory loggerFactory) =>
{
    var request = await http.Request.ReadFromJsonAsync<ClassificationRequestBody>();
    if (request == null || string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { error = "text is required" });
    }

    var start = System.Diagnostics.Stopwatch.StartNew();
    var result = await queue.EnqueueAndWaitAsync(request.Text, http.RequestAborted);
    start.Stop();
    result.DurationMs = start.Elapsed.TotalMilliseconds;
    analytics.Record(result.Verdict.ToString(), result.Blocked, result.DurationMs);
    var logger = loggerFactory.CreateLogger("Classification");
    var verdictStyled = Ansi.Colorize(LogStyle.Glyph(result.Verdict) + " " + result.Verdict, result.Color);
    var labelStyled = Ansi.Colorize(result.PredictedLabel, result.Color);
    string pct(decimal d) => (d * 100m).ToString("0.00");
    var topScores = string.Join("/", result.Scores.Take(2).Select(s => $"{s.Label}:{pct(s.Score)}%"));
    var minimal = (Environment.GetEnvironmentVariable("SAKI_ML_LOG_MINIMAL") ?? "true").Equals("true", StringComparison.OrdinalIgnoreCase);
    if (minimal && !logger.IsEnabled(LogLevel.Debug))
    {
        logger.LogInformation("{Verdict} {Label} {Pct}% {Latency}ms [{Scores}] :: {Text}", verdictStyled, labelStyled, pct(result.Confidence), Math.Round(result.DurationMs, 2), topScores, TextUtils.Truncate(request.Text));
    }
    else
    {
        var ip = http.Connection.RemoteIpAddress?.ToString() ?? "-";
        var reqId = http.TraceIdentifier;
        logger.LogInformation("{Verdict} {Label} {Pct}% {Latency}ms [{Scores}] ip={IP} id={Id} :: {Text}", verdictStyled, labelStyled, pct(result.Confidence), Math.Round(result.DurationMs, 2), topScores, ip, reqId, TextUtils.Truncate(request.Text));
    }
    if (result.Verdict == ClassificationVerdict.Unsure)
    {
        insights.RecordUnsure(request.Text, result);
    }
    return Results.Ok(result);
});

// Analytics endpoints
app.MapGet("/analytics", (IAnalyticsService analytics) => Results.Ok(analytics.Snapshot()))
   .WithName("AnalyticsSnapshot");
app.MapGet("/analytics/last/{minutes:int}", (int minutes, IAnalyticsService analytics) =>
{
    var period = TimeSpan.FromMinutes(Math.Max(1, minutes));
    return Results.Ok(analytics.Snapshot(period));
});

// Configuration diagnostics
app.MapGet("/config/diagnostics", (IConfigurationDiagnostics diag) => Results.Ok(diag.Inspect()));

// Unsure insights endpoints
app.MapGet("/insights/unsure", (IInsightsBuffer insights, int take) => Results.Ok(insights.GetRecent(take <= 0 ? 50 : take)));
app.MapGet("/insights/unsure/last/{minutes:int}", (IInsightsBuffer insights, int minutes, int take) =>
{
    var period = TimeSpan.FromMinutes(Math.Max(1, minutes));
    return Results.Ok(insights.GetRecent(take <= 0 ? 50 : take, period));
});

app.Run();

static LogLevel? ParseLogLevel(string? value)
{
    if (string.IsNullOrWhiteSpace(value)) return null;
    return value.Trim().ToLowerInvariant() switch
    {
        "trace" => LogLevel.Trace,
        "debug" => LogLevel.Debug,
        "information" or "info" => LogLevel.Information,
        "warning" or "warn" => LogLevel.Warning,
        "error" => LogLevel.Error,
        "critical" or "fatal" => LogLevel.Critical,
        "none" => LogLevel.None,
        _ => null
    };
}

// moved truncate to Saki_ML.Utils.TextUtils

