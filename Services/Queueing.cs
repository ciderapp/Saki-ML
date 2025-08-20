using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Saki_ML.Contracts;

namespace Saki_ML.Services
{
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
                catch (System.Exception ex)
                {
                    _logger.LogError(ex, "Classification failed");
                    req.Completion.TrySetException(ex);
                }
            }
        }
    }
}


