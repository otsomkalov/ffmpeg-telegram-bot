using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Bot.Models;
using Bot.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bot.Services
{
    public class CleanerService : BackgroundService
    {
        private readonly QueueClient _cleanerQueue;
        private readonly ILogger<CleanerService> _logger;
        private readonly ServicesSettings _servicesSettings;

        public CleanerService(IQueueFactory queueFactory, ILogger<CleanerService> logger, IOptions<ServicesSettings> servicesSettings)
        {
            _logger = logger;
            _cleanerQueue = queueFactory.GetQueue(Queue.Cleaner);
            _servicesSettings = servicesSettings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunAsync(stoppingToken);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error during Cleaner execution:");
                }

                await Task.Delay(_servicesSettings.ProcessingDelay, stoppingToken);
            }
        }

        private async Task RunAsync(CancellationToken stoppingToken)
        {
            var response = await _cleanerQueue.ReceiveMessageAsync(cancellationToken: stoppingToken);
            var queueMessage = response.Value;

            if (queueMessage is null) return;

            var (inputFilePath, outputFilePath, thumbnailFilePath) = JsonSerializer.Deserialize<CleanerMessage>(queueMessage.Body)!;

            CleanupFiles(inputFilePath, outputFilePath, thumbnailFilePath);

            await _cleanerQueue.DeleteMessageAsync(queueMessage.MessageId, queueMessage.PopReceipt, stoppingToken);
        }

        private static void CleanupFiles(string inputFilePath, string outputFilePath, string thumbnailFilePath)
        {
            if (File.Exists(inputFilePath))
            {
                File.Delete(inputFilePath);
            }

            if (File.Exists(outputFilePath))
            {
                File.Delete(outputFilePath);
            }

            if (File.Exists(thumbnailFilePath))
            {
                File.Delete(thumbnailFilePath);
            }
        }
    }
}
