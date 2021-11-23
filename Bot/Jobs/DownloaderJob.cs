using System.Net;
using Bot.Constants;
using Telegram.Bot.Exceptions;
using Microsoft.Extensions.Options;
using File = System.IO.File;
using Message = Telegram.Bot.Types.Message;

namespace Bot.Jobs;

[DisallowConcurrentExecution]
public class DownloaderJob : IJob
{
    private readonly IAmazonSQS _sqsClient;
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<DownloaderJob> _logger;
    private readonly ServicesSettings _servicesSettings;
    private readonly IHttpClientFactory _clientFactory;

    public DownloaderJob(ITelegramBotClient bot, ILogger<DownloaderJob> logger,
        IOptions<ServicesSettings> servicesSettings, IAmazonSQS sqsClient, IHttpClientFactory clientFactory)
    {
        _bot = bot;
        _logger = logger;
        _sqsClient = sqsClient;
        _clientFactory = clientFactory;
        _servicesSettings = servicesSettings.Value;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var response = await _sqsClient.ReceiveMessageAsync(_servicesSettings.DownloaderQueueUrl);
        var queueMessage = response.Messages.FirstOrDefault();

        if (queueMessage != null)
        {
            var (receivedMessage, sentMessage, link) = JsonSerializer.Deserialize<DownloaderMessage>(queueMessage.Body)!;

            try
            {
                if (sentMessage.Date < DateTime.UtcNow.AddDays(-2))
                {
                    sentMessage = await _bot.SendTextMessageAsync(new(receivedMessage.Chat.Id),
                        "Downloading file 🚀",
                        replyToMessageId: receivedMessage.MessageId,
                        disableNotification: true);
                }
                else
                {
                    await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                        sentMessage.MessageId,
                        "Downloading file 🚀");
                }

                var inputFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.webm");

                if (string.IsNullOrEmpty(link))
                {
                    await HandleDocumentAsync(receivedMessage, sentMessage, inputFilePath);
                }
                else
                {
                    await HandleLinkAsync(receivedMessage, sentMessage, link, inputFilePath);
                }

                await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    "Your file is waiting to be converted 🕒");

                await _sqsClient.DeleteMessageAsync(_servicesSettings.DownloaderQueueUrl, queueMessage.ReceiptHandle);
            }
            catch (ApiRequestException telegramException)
            {
                _logger.LogError(telegramException, "Telegram error during Uploader execution:");
                await _sqsClient.DeleteMessageAsync(_servicesSettings.DownloaderQueueUrl, queueMessage.ReceiptHandle);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error during Downloader execution:");
            }
        }
    }

    private async Task HandleLinkAsync(Message receivedMessage, Message sentMessage, string linkOrFileName, string inputFilePath)
    {
        using var client = _clientFactory.CreateClient();
        await using var fileStream = File.Create(inputFilePath);

        using var response = await client.GetAsync(linkOrFileName);

        switch (response.StatusCode)
        {
            case HttpStatusCode.Unauthorized:

                await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{linkOrFileName}\nI am not authorized to download video from this source 🚫");

                return;

            case HttpStatusCode.NotFound:

                await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{linkOrFileName}\nVideo not found ⚠️");

                return;

            case HttpStatusCode.InternalServerError:

                await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{linkOrFileName}\nServer error 🛑");

                return;
        }

        await response.Content.CopyToAsync(fileStream);

        await SendMessageAsync(receivedMessage, sentMessage, inputFilePath);
    }

    private async Task HandleDocumentAsync(Message receivedMessage, Message sentMessage, string inputFileName)
    {
        await using (var fileStream = File.Create(inputFileName))
        {
            await _bot.GetInfoAndDownloadFileAsync(receivedMessage.Document.FileId, fileStream);
        }

        await SendMessageAsync(receivedMessage, sentMessage, inputFileName);
    }

    private async Task SendMessageAsync(Message receivedMessage, Message sentMessage, string inputFilePath)
    {
        var converterMessage = new ConverterMessage(receivedMessage, sentMessage, inputFilePath);

        await _sqsClient.SendMessageAsync(_servicesSettings.ConverterQueueUrl,
            JsonSerializer.Serialize(converterMessage, JsonSerializerConstants.SerializerOptions));
    }
}