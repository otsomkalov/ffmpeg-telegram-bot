using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Amazon.SQS;
using Bot.Constants;
using Bot.Models;
using Bot.Services;
using Bot.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Bot.Jobs
{
    [DisallowConcurrentExecution]
    public class ConverterJob : IJob
    {
        private readonly IAmazonSQS _sqsClient;
        private readonly ITelegramBotClient _bot;
        private readonly ILogger<ConverterJob> _logger;
        private readonly ServicesSettings _servicesSettings;
        private readonly FFMpegService _ffMpegService;

        public ConverterJob(ITelegramBotClient bot, ILogger<ConverterJob> logger, IOptions<ServicesSettings> servicesSettings,
            IAmazonSQS sqsClient, FFMpegService ffMpegService)
        {
            _bot = bot;
            _logger = logger;
            _sqsClient = sqsClient;
            _ffMpegService = ffMpegService;
            _servicesSettings = servicesSettings.Value;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            var receiveMessageResponse = await _sqsClient.ReceiveMessageAsync(_servicesSettings.ConverterQueueUrl);
            var queueMessage = receiveMessageResponse.Messages.FirstOrDefault();

            if (queueMessage != null)
            {
                var (receivedMessage, sentMessage, inputFilePath) = JsonSerializer.Deserialize<ConverterMessage>(queueMessage.Body)!;

                try
                {
                    await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                        sentMessage.MessageId,
                        "Conversion in progress 🚀");

                    var outputFilePath = await _ffMpegService.ConvertAsync(inputFilePath);

                    await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                        sentMessage.MessageId,
                        "Generating thumbnail 🖼️");

                    var thumbnailFilePath = await _ffMpegService.GetThumbnailAsync(outputFilePath);

                    var uploaderMessage = new UploaderMessage(receivedMessage, sentMessage, inputFilePath, outputFilePath,
                        thumbnailFilePath);

                    await _sqsClient.SendMessageAsync(_servicesSettings.UploaderQueueUrl,
                        JsonSerializer.Serialize(uploaderMessage, JsonSerializerConstants.SerializerOptions));

                    await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                        sentMessage.MessageId,
                        "Your file is waiting to be uploaded 🕒");

                    await _sqsClient.DeleteMessageAsync(_servicesSettings.ConverterQueueUrl,
                        queueMessage.ReceiptHandle);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error during Converter execution:");

                    await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                        sentMessage.MessageId,
                        "Error during conversion!");
                }
            }
        }
    }
}