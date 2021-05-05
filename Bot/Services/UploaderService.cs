using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Bot.Models;
using Bot.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using File = System.IO.File;

namespace Bot.Services
{
    public class UploaderService : BackgroundService
    {
        private readonly ITelegramBotClient _bot;
        private readonly QueueClient _uploaderQueue;
        private readonly QueueClient _cleanerQueue;
        private readonly ILogger<UploaderService> _logger;
        private readonly ServicesSettings _servicesSettings;

        public UploaderService(ITelegramBotClient bot, IQueueFactory queueFactory, ILogger<UploaderService> logger,
            IOptions<ServicesSettings> servicesSettings)
        {
            _bot = bot;
            _logger = logger;
            _servicesSettings = servicesSettings.Value;
            _uploaderQueue = queueFactory.GetQueue(Queue.Uploader);
            _cleanerQueue = queueFactory.GetQueue(Queue.Cleaner);
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
                    _logger.LogError(e, "Error during Uploader execution:");
                }

                await Task.Delay(_servicesSettings.ProcessingDelay, stoppingToken);
            }
        }

        private async Task RunAsync(CancellationToken stoppingToken)
        {
            var response = await _uploaderQueue.ReceiveMessageAsync(cancellationToken: stoppingToken);
            var queueMessage = response.Value;

            if (queueMessage is null) return;

            var (receivedMessage, sentMessage, inputFilePath, outputFilePath, thumbnailFilePath, linkOrFileName) =
                JsonSerializer.Deserialize<UploaderMessage>(queueMessage.Body)!;

            await _bot.EditMessageTextAsync(
                new(sentMessage.Chat.Id),
                sentMessage.MessageId,
                $"{linkOrFileName}\nYour file is uploading 🚀",
                cancellationToken: stoppingToken);

            try
            {
                await using var videoStream = File.OpenRead(outputFilePath);
                await using var imageStream = File.OpenRead(thumbnailFilePath);

                await _bot.DeleteMessageAsync(
                    new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    stoppingToken);

                await _bot.SendVideoAsync(
                    new(sentMessage.Chat.Id),
                    new InputMedia(videoStream, outputFilePath),
                    replyToMessageId: receivedMessage.MessageId,
                    thumb: new(imageStream, thumbnailFilePath),
                    caption: linkOrFileName,
                    disableNotification: true,
                    cancellationToken: stoppingToken);
            }
            catch (Exception)
            {
                await _bot.EditMessageTextAsync(
                    new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{linkOrFileName}\nError during file upload",
                    cancellationToken: stoppingToken);

                await SendCleanerMessageAsync(inputFilePath, outputFilePath, thumbnailFilePath);

                throw;
            }

            await SendCleanerMessageAsync(inputFilePath, outputFilePath, thumbnailFilePath);

            await _uploaderQueue.DeleteMessageAsync(queueMessage.MessageId, queueMessage.PopReceipt, stoppingToken);

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }

        private async Task SendCleanerMessageAsync(string inputFilePath, string outputFilePath = null, string thumbnailFilePath = null)
        {
            var cleanerMessage = new CleanerMessage(inputFilePath, outputFilePath, thumbnailFilePath);

            await _cleanerQueue.SendMessageAsync(JsonSerializer.Serialize(cleanerMessage));
        }
    }
}
