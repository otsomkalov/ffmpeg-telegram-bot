using Bot.Constants;
using Microsoft.Extensions.Options;
using Telegram.Bot.Exceptions;
using File = System.IO.File;

namespace Bot.BackgroundServices;

public class Uploader : BackgroundService
{
    private readonly ITelegramBotClient _bot;
    private readonly IAmazonSQS _sqsClient;
    private readonly ILogger<Uploader> _logger;
    private readonly ServicesSettings _servicesSettings;

    public Uploader(ITelegramBotClient bot, ILogger<Uploader> logger,
        IOptions<ServicesSettings> servicesSettings, IAmazonSQS sqsClient)
    {
        _bot = bot;
        _logger = logger;
        _sqsClient = sqsClient;
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
                _logger.LogError(telegramException, "Telegram error during Uploader execution:");
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error during Uploader execution:");
            }

            await Task.Delay(_servicesSettings.Delay, stoppingToken);
        }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var response = await _sqsClient.ReceiveMessageAsync(_servicesSettings.UploaderQueueUrl, cancellationToken);
        var queueMessage = response.Messages.FirstOrDefault();

        if (queueMessage == null)
        {
            return;
        }

        var (receivedMessage, sentMessage, inputFilePath, outputFilePath, thumbnailFilePath) =
            JsonSerializer.Deserialize<UploaderMessage>(queueMessage.Body)!;

        await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
            sentMessage.MessageId,
            "Your file is uploading 🚀", cancellationToken: cancellationToken);

        await using var videoStream = File.OpenRead(outputFilePath);
        await using var imageStream = File.OpenRead(thumbnailFilePath);

        await _bot.DeleteMessageAsync(new(sentMessage.Chat.Id),
            sentMessage.MessageId, cancellationToken);

        await _bot.SendVideoAsync(new(sentMessage.Chat.Id),
            new InputMedia(videoStream, outputFilePath),
            caption: "🇺🇦 Help the Ukrainian army fight russian and belarus invaders: https://savelife.in.ua/en/donate/",
            replyToMessageId: receivedMessage.MessageId,
            thumb: new(imageStream, thumbnailFilePath),
            disableNotification: true, cancellationToken: cancellationToken);

        var cleanerMessage = new CleanerMessage(inputFilePath, outputFilePath, thumbnailFilePath);

        await _sqsClient.SendMessageAsync(_servicesSettings.CleanerQueueUrl,
            JsonSerializer.Serialize(cleanerMessage, JsonSerializerConstants.SerializerOptions), cancellationToken);

        await _sqsClient.DeleteMessageAsync(_servicesSettings.UploaderQueueUrl, queueMessage.ReceiptHandle, cancellationToken);
    }
}