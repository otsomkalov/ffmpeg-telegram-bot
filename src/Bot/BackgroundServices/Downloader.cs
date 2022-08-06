using System.Net;
using Bot.Constants;
using Microsoft.Extensions.Options;
using Telegram.Bot.Exceptions;
using File = System.IO.File;

namespace Bot.BackgroundServices;

public class Downloader : BackgroundService
{
    private readonly IAmazonSQS _sqsClient;
    private readonly ITelegramBotClient _bot;
    private readonly ILogger<Downloader> _logger;
    private readonly ServicesSettings _servicesSettings;
    private readonly IHttpClientFactory _clientFactory;

    public Downloader(ITelegramBotClient bot, ILogger<Downloader> logger,
        IOptions<ServicesSettings> servicesSettings, IAmazonSQS sqsClient, IHttpClientFactory clientFactory)
    {
        _bot = bot;
        _logger = logger;
        _sqsClient = sqsClient;
        _clientFactory = clientFactory;
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
                _logger.LogError(e, "Error during Downloader execution:");
            }

            await Task.Delay(_servicesSettings.Delay, stoppingToken);
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var response = await _sqsClient.ReceiveMessageAsync(_servicesSettings.DownloaderQueueUrl, cancellationToken);
        var queueMessage = response.Messages.FirstOrDefault();

        if (queueMessage == null)
        {
            return;
        }

        var (receivedMessage, sentMessage, link, downloaderMessageType) = JsonSerializer.Deserialize<DownloaderMessage>(queueMessage.Body)!;

        try
        {
            if (sentMessage.Date < DateTime.UtcNow.AddDays(-2))
            {
                sentMessage = await _bot.SendTextMessageAsync(new(receivedMessage.Chat.Id),
                    "Downloading file 🚀",
                    replyToMessageId: receivedMessage.MessageId,
                    disableNotification: true, cancellationToken: cancellationToken);
            }
            else
            {
                await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    "Downloading file 🚀", cancellationToken: cancellationToken);
            }

            var inputFilePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.webm");

            var handleMessageTask = downloaderMessageType switch
            {
                DownloaderMessageType.Link => HandleLinkAsync(receivedMessage, sentMessage, link, inputFilePath, cancellationToken),
                DownloaderMessageType.Video => HandleFileBaseAsync(receivedMessage, sentMessage, inputFilePath,
                    receivedMessage.Video!.FileId, cancellationToken),
                DownloaderMessageType.Document => HandleFileBaseAsync(receivedMessage, sentMessage, inputFilePath,
                    receivedMessage.Document!.FileId, cancellationToken),
            };

            await handleMessageTask;

            await _sqsClient.DeleteMessageAsync(_servicesSettings.DownloaderQueueUrl, queueMessage.ReceiptHandle, cancellationToken);
        }
        catch (ApiRequestException telegramException)
        {
            _logger.LogError(telegramException, "Telegram error during Uploader execution:");
            await _sqsClient.DeleteMessageAsync(_servicesSettings.DownloaderQueueUrl, queueMessage.ReceiptHandle, cancellationToken);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error during Downloader execution:");
        }
    }

    private async Task HandleLinkAsync(Message receivedMessage, Message sentMessage, string linkOrFileName, string inputFilePath,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, linkOrFileName);

        using var client = _clientFactory.CreateClient(nameof(Downloader));
        using var response = await client.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode switch
            {
                HttpStatusCode.Unauthorized => $"{linkOrFileName}\nI am not authorized to download video from this source 🚫",
                HttpStatusCode.Forbidden => $"{linkOrFileName}\nMy access to this video is forbidden 🚫",
                HttpStatusCode.NotFound => $"{linkOrFileName}\nVideo not found ⚠️",
                HttpStatusCode.InternalServerError => $"{linkOrFileName}\nServer error 🛑",
                HttpStatusCode.ServiceUnavailable => $"{linkOrFileName}\nService with file reported unavailability. Try to convert your video later.",
                _ => $"{linkOrFileName}\nService responded with {response.StatusCode} status code to request to the file."
            };

            await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
                sentMessage.MessageId,
                message, cancellationToken: cancellationToken);

            return;
        }

        await using var fileStream = File.Create(inputFilePath);

        await response.Content.CopyToAsync(fileStream, cancellationToken);

        await SendMessageAsync(receivedMessage, sentMessage, inputFilePath, cancellationToken);
    }

    private async Task HandleFileBaseAsync(Message receivedMessage, Message sentMessage, string inputFileName,
        string fileId, CancellationToken cancellationToken)
    {
        await using (var fileStream = File.Create(inputFileName))
        {
            await _bot.GetInfoAndDownloadFileAsync(fileId, fileStream, cancellationToken);
        }

        await SendMessageAsync(receivedMessage, sentMessage, inputFileName, cancellationToken);
    }

    private async Task SendMessageAsync(Message receivedMessage, Message sentMessage, string inputFilePath,
        CancellationToken cancellationToken)
    {
        var converterMessage = new ConverterMessage(receivedMessage, sentMessage, inputFilePath);

        await _sqsClient.SendMessageAsync(_servicesSettings.ConverterQueueUrl,
            JsonSerializer.Serialize(converterMessage, JsonSerializerConstants.SerializerOptions), cancellationToken);

        await _bot.EditMessageTextAsync(new(sentMessage.Chat.Id),
            sentMessage.MessageId,
            "Your file is waiting to be converted 🕒", cancellationToken: cancellationToken);
    }
}