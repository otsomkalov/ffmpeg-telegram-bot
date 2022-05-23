using Bot.Constants;
using Microsoft.Extensions.Options;
using Telegram.Bot.Exceptions;

namespace Bot.BackgroundServices;

public class Converter : BackgroundService
{
    private readonly IAmazonSQS _sqsClient;
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<Converter> _logger;
    private readonly ServicesSettings _servicesSettings;
    private readonly FFMpegService _ffMpegService;

    public Converter(ITelegramBotClient bot, ILogger<Converter> logger, IOptions<ServicesSettings> servicesSettings,
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
            try
            {
                await RunAsync(stoppingToken);
            }
            catch (ApiRequestException telegramException)
            {
                _logger.LogError(telegramException, "Telegram error during Converter execution:");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error during Converter execution:");
            }

            await Task.Delay(_servicesSettings.Delay, stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var receiveMessageResponse = await _sqsClient.ReceiveMessageAsync(_servicesSettings.ConverterQueueUrl, cancellationToken);
        var queueMessage = receiveMessageResponse.Messages.FirstOrDefault();

        if (queueMessage == null)
        {
            return;
        }

        var (receivedMessage, sentMessage, inputFilePath) = JsonSerializer.Deserialize<ConverterMessage>(queueMessage.Body)!;

        await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
            sentMessage.MessageId,
            "Conversion in progress 🚀", cancellationToken: cancellationToken);

        var outputFilePath = await _ffMpegService.ConvertAsync(inputFilePath);

        if (outputFilePath == null)
        {
            await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id), sentMessage.MessageId, "Conversion failed 😱",
                cancellationToken: cancellationToken);

            return;
        }

        await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
            sentMessage.MessageId,
            "Generating thumbnail 🖼️", cancellationToken: cancellationToken);

        var thumbnailFilePath = await _ffMpegService.GetThumbnailAsync(outputFilePath);

        var uploaderMessage = new UploaderMessage(receivedMessage, sentMessage, inputFilePath, outputFilePath,
            thumbnailFilePath);

        await _sqsClient.SendMessageAsync(_servicesSettings.UploaderQueueUrl,
            JsonSerializer.Serialize(uploaderMessage, JsonSerializerConstants.SerializerOptions), cancellationToken);

        await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
            sentMessage.MessageId,
            "Your file is waiting to be uploaded 🕒", cancellationToken: cancellationToken);

        await _sqsClient.DeleteMessageAsync(_servicesSettings.ConverterQueueUrl,
            queueMessage.ReceiptHandle, cancellationToken);
    }
}