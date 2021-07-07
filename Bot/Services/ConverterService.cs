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

namespace Bot.Services
{
    public class ConverterService : BackgroundService
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly ITelegramBotClient _bot;
        private readonly ILogger<ConverterService> _logger;
        private readonly ServicesSettings _servicesSettings;
        private readonly FFMpegService _ffMpegService;

        public ConverterService(ITelegramBotClient bot, ILogger<ConverterService> logger, IOptions<ServicesSettings> servicesSettings,
            IAmazonSQS sqsClient, FFMpegService ffMpegService)
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
                var receiveMessageResponse = await _sqsClient.ReceiveMessageAsync(_servicesSettings.ConverterQueueUrl, stoppingToken);
                var queueMessage = receiveMessageResponse.Messages.FirstOrDefault();

                if (queueMessage != null)
                {
                    var (receivedMessage, sentMessage, inputFilePath) = JsonSerializer.Deserialize<ConverterMessage>(queueMessage.Body)!;

                    try
                    {
                        await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                            sentMessage.MessageId,
                            "Conversion in progress 🚀",
                            cancellationToken: stoppingToken);

                        var outputFilePath = await _ffMpegService.ConvertAsync(inputFilePath);

                        await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                            sentMessage.MessageId,
                            "Generating thumbnail 🖼️",
                            cancellationToken: stoppingToken);

                        var thumbnailFilePath = await _ffMpegService.GetThumbnailAsync(outputFilePath);

                        var uploaderMessage = new UploaderMessage(receivedMessage, sentMessage, inputFilePath, outputFilePath,
                            thumbnailFilePath);

                        await _sqsClient.SendMessageAsync(_servicesSettings.UploaderQueueUrl,
                            JsonSerializer.Serialize(uploaderMessage, JsonSerializerConstants.SerializerOptions),
                            stoppingToken);

                        await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                            sentMessage.MessageId,
                            "Your file is waiting to be uploaded 🕒",
                            cancellationToken: stoppingToken);

                        await _sqsClient.DeleteMessageAsync(_servicesSettings.ConverterQueueUrl,
                            queueMessage.ReceiptHandle,
                            stoppingToken);
                    }
                    catch (Exception e)
                    {
                        _logger.LogError(e, "Error during Converter execution:");

                        await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                            sentMessage.MessageId,
                            "Error during file conversion!",
                            cancellationToken: stoppingToken);
                    }
                }

                await Task.Delay(_servicesSettings.ProcessingDelay, stoppingToken);
            }
        }
    }
}
