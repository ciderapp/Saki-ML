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

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHostedService<ClassificationWorker>();

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

app.MapPost("/classify", async (HttpContext http, IClassificationQueue queue) =>
{
    var request = await http.Request.ReadFromJsonAsync<ClassificationRequestBody>();
    if (request == null || string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { error = "text is required" });
    }

    var result = await queue.EnqueueAndWaitAsync(request.Text, http.RequestAborted);
    return Results.Ok(result);
});

app.Run();

public record ClassificationRequestBody(string Text);

public interface IClassificationQueue
{
    Task<ClassificationResult> EnqueueAndWaitAsync(string text, CancellationToken cancellationToken);
}

public sealed class ChannelClassificationQueue : IClassificationQueue
{
    private readonly Channel<ClassificationRequest> _channel;

    public ChannelClassificationQueue(Channel<ClassificationRequest> channel)
    {
        _channel = channel;
    }

    public async Task<ClassificationResult> EnqueueAndWaitAsync(string text, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<ClassificationResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new ClassificationRequest(text, tcs);
        await _channel.Writer.WriteAsync(request, cancellationToken);
        return await tcs.Task.WaitAsync(cancellationToken);
    }
}

public sealed record ClassificationRequest(string Text, TaskCompletionSource<ClassificationResult> Completion);

public sealed class ClassificationWorker : BackgroundService
{
    private readonly Channel<ClassificationRequest> _channel;
    private readonly IClassificationService _service;
    private readonly ILogger<ClassificationWorker> _logger;

    public ClassificationWorker(Channel<ClassificationRequest> channel, IClassificationService service, ILogger<ClassificationWorker> logger)
    {
        _channel = channel;
        _service = service;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var req in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                var result = _service.Classify(req.Text);
                req.Completion.TrySetResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Classification failed");
                req.Completion.TrySetException(ex);
            }
        }
    }
}

public interface IClassificationService
{
    ClassificationResult Classify(string text);
}

public sealed class MlNetClassificationService : IClassificationService
{
    public ClassificationResult Classify(string text)
    {
        var input = new SpamClassifier.ModelInput { Label = string.Empty, Text = text };
        var output = SpamClassifier.Predict(input);
        var scores = SpamClassifier.GetSortedScoresWithLabels(output)
            .Select(kv => new KeyValuePair<string, decimal>(kv.Key, ClampScoreDecimal(kv.Value)))
            .ToArray();
        var top = scores.FirstOrDefault();
        return new ClassificationResult
        {
            PredictedLabel = output.PredictedLabel,
            Confidence = top.Value,
            Scores = scores.Select(kv => new LabelScore(kv.Key, kv.Value)).ToArray()
        };
    }

    private static decimal ClampScoreDecimal(float score)
    {
        // Clamp to [0, 1] and round to 7 decimal places
        var clamped = Math.Clamp(score, 0f, 1f);
        var asDecimal = (decimal)clamped;
        return Math.Round(asDecimal, 7, MidpointRounding.AwayFromZero);
    }
}

public sealed record LabelScore(string Label, decimal Score);

public sealed class ClassificationResult
{
    public string PredictedLabel { get; set; } = string.Empty;
    public decimal Confidence { get; set; }
    public LabelScore[] Scores { get; set; } = Array.Empty<LabelScore>();
}
