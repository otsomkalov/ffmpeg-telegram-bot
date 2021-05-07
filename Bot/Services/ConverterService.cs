using System;
using System.IO;
using System.Linq;
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
using Xabe.FFmpeg;

namespace Bot.Services
{
    public class ConverterService : BackgroundService
    {
        private readonly QueueClient _converterQueue;
        private readonly QueueClient _uploaderQueue;
        private readonly QueueClient _cleanerQueue;
        private readonly ITelegramBotClient _bot;
        private readonly ILogger<ConverterService> _logger;
        private readonly ServicesSettings _servicesSettings;

        public ConverterService(IQueueFactory queueFactory, ITelegramBotClient bot, ILogger<ConverterService> logger,
            IOptions<ServicesSettings> servicesSettings)
        {
            _bot = bot;
            _logger = logger;
            _converterQueue = queueFactory.GetQueue(Queue.Converter);
            _uploaderQueue = queueFactory.GetQueue(Queue.Uploader);
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
                    _logger.LogError(e, "Error during Converter execution:");
                }

                await Task.Delay(_servicesSettings.ProcessingDelay, stoppingToken);
            }
        }

        private async Task RunAsync(CancellationToken stoppingToken)
        {
            var response = await _converterQueue.ReceiveMessageAsync(cancellationToken: stoppingToken);
            var queueMessage = response.Value;

            if (queueMessage is null) return;

            var (receivedMessage, sentMessage, inputFilePath, linkOrFilename) =
                JsonSerializer.Deserialize<ConverterMessage>(queueMessage.Body)!;

            try
            {
                await _bot.EditMessageTextAsync(
                    new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{linkOrFilename}\nConversion in progress 🚀",
                    cancellationToken: stoppingToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error during updating message:");
            }

            var mediaInfo = await FFmpeg.GetMediaInfo(inputFilePath, stoppingToken);

            var videoStream = mediaInfo.VideoStreams.FirstOrDefault();

            if (videoStream == null)
            {
                await _bot.EditMessageTextAsync(
                    new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{linkOrFilename}\nVideo doesn't have video stream inside",
                    cancellationToken: stoppingToken);

                return;
            }

            var width = videoStream.Width % 2 == 0 ? videoStream.Width : videoStream.Width - 1;
            var height = videoStream.Height % 2 == 0 ? videoStream.Height : videoStream.Height - 1;

            videoStream = videoStream
                .SetCodec(VideoCodec.h264)
                .SetSize(width, height);

            var audioStream = mediaInfo.AudioStreams.FirstOrDefault()?.SetCodec(AudioCodec.aac);

            var outputFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.mp4");

            try
            {
                await FFmpeg.Conversions.New()
                    .AddStream<IStream>(videoStream, audioStream)
                    .SetOutput(outputFilePath)
                    .Start(stoppingToken);
            }
            catch (Exception)
            {
                await _bot.EditMessageTextAsync(
                    new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{linkOrFilename}\nError during file conversion",
                    cancellationToken: stoppingToken);

                await _converterQueue.DeleteMessageAsync(
                    queueMessage.MessageId,
                    queueMessage.PopReceipt,
                    stoppingToken);
                
                await SendCleanerMessageAsync(inputFilePath);

                throw;
            }

            await _bot.EditMessageTextAsync(
                new(sentMessage.Chat.Id),
                sentMessage.MessageId,
                $"{linkOrFilename}\nGenerating thumbnail 🖼️",
                cancellationToken: stoppingToken);

            var thumbnailFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.jpg");

            var thumbnailConversion = await FFmpeg.Conversions.FromSnippet.Snapshot(
                outputFilePath,
                thumbnailFilePath,
                TimeSpan.Zero);

            try
            {
                await thumbnailConversion.Start(stoppingToken);
            }
            catch (Exception)
            {
                await _bot.EditMessageTextAsync(
                    new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{linkOrFilename}\nError during file conversion",
                    cancellationToken: stoppingToken);

                await _converterQueue.DeleteMessageAsync(
                    queueMessage.MessageId,
                    queueMessage.PopReceipt,
                    stoppingToken);
                
                await SendCleanerMessageAsync(inputFilePath, outputFilePath);

                throw;
            }

            await SendMessageAsync(receivedMessage, sentMessage, inputFilePath, outputFilePath, thumbnailFilePath, linkOrFilename);

            await _converterQueue.DeleteMessageAsync(
                queueMessage.MessageId,
                queueMessage.PopReceipt,
                stoppingToken);

            await _bot.EditMessageTextAsync(
                new(sentMessage.Chat.Id),
                sentMessage.MessageId,
                $"{linkOrFilename}\nYour file is waiting to be uploaded 🕒",
                cancellationToken: stoppingToken);
        }

        private async Task SendMessageAsync(Message receivedMessage, Message sentMessage, string inputFilePath,
            string outputFilePath, string thumbnailFilePath, string linkOrFileName)
        {
            var uploaderMessage = new UploaderMessage(
                receivedMessage,
                sentMessage,
                inputFilePath,
                outputFilePath,
                thumbnailFilePath,
                linkOrFileName);

            await _uploaderQueue.SendMessageAsync(JsonSerializer.Serialize(uploaderMessage));
        }

        private async Task SendCleanerMessageAsync(string inputFilePath, string outputFilePath = null,
            string thumbnailFilePath = null)
        {
            var cleanerMessage = new CleanerMessage(inputFilePath, outputFilePath, thumbnailFilePath);

            await _cleanerQueue.SendMessageAsync(JsonSerializer.Serialize(cleanerMessage));
        }
    }
}
