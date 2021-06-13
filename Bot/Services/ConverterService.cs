using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Bot.Constants;
using Bot.Models;
using Bot.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bot.Services
{
    public class ConverterService : BackgroundService
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly ITelegramBotClient _bot;
        private readonly ILogger<ConverterService> _logger;
        private readonly ServicesSettings _servicesSettings;
        private readonly FFMpegService _ffMpegService;

        public ConverterService(ITelegramBotClient bot, ILogger<ConverterService> logger,
            IOptions<ServicesSettings> servicesSettings, IAmazonSQS sqsClient, FFMpegService ffMpegService)
        {
            _bot = bot;
            _logger = logger;
            _sqsClient = sqsClient;
            _ffMpegService = ffMpegService;
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
            var response = await _sqsClient.ReceiveMessageAsync(_servicesSettings.ConverterQueueUrl, stoppingToken);
            var queueMessage = response.Messages.FirstOrDefault();

            if (queueMessage is null) return;

            var (receivedMessage, sentMessage, inputFilePath, linkOrFilename) =
                JsonSerializer.Deserialize<ConverterMessage>(queueMessage.Body)!;

            try
            {
                await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{linkOrFilename}\nConversion in progress 🚀",
                    cancellationToken: stoppingToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error during updating message:");
            }

            string outputFilePath;

            try
            {
                outputFilePath = await _ffMpegService.ConvertAsync(inputFilePath);
            }
            catch (Exception)
            {
                await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{linkOrFilename}\nError during file conversion",
                    cancellationToken: stoppingToken);

                await SendCleanerMessageAsync(inputFilePath);

                throw;
            }

            try
            {
                await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{linkOrFilename}\nGenerating thumbnail 🖼️",
                    cancellationToken: stoppingToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error during updating message:");
            }

            string thumbnailFilePath;

            try
            {
                thumbnailFilePath = await _ffMpegService.GetThumbnailAsync(outputFilePath);
            }
            catch (Exception)
            {
                await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{linkOrFilename}\nError during thumbnail generation",
                    cancellationToken: stoppingToken);

                await SendCleanerMessageAsync(inputFilePath, outputFilePath);

                throw;
            }

            await SendMessageAsync(receivedMessage, sentMessage, inputFilePath, outputFilePath, thumbnailFilePath, linkOrFilename);

            try
            {
                await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{linkOrFilename}\nYour file is waiting to be uploaded 🕒",
                    cancellationToken: stoppingToken);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error during updating message:");
            }

            await _sqsClient.DeleteMessageAsync(_servicesSettings.ConverterQueueUrl,
                queueMessage.ReceiptHandle,
                stoppingToken);
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

            await _sqsClient.SendMessageAsync(_servicesSettings.UploaderQueueUrl,
                JsonSerializer.Serialize(uploaderMessage, JsonSerializerConstants.SerializerOptions));
        }

        private async Task SendCleanerMessageAsync(string inputFilePath, string outputFilePath = null,
            string thumbnailFilePath = null)
        {
            var cleanerMessage = new CleanerMessage(inputFilePath, outputFilePath, thumbnailFilePath);

            await _sqsClient.SendMessageAsync(_servicesSettings.CleanerQueueUrl,
                JsonSerializer.Serialize(cleanerMessage, JsonSerializerConstants.SerializerOptions));
        }
    }
}
