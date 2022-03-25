using System.Text.RegularExpressions;
using Bot.Constants;
using Bot.Extensions;
using Microsoft.Extensions.Options;
using Message = Telegram.Bot.Types.Message;

namespace Bot.Services;

public class MessageService
{
    private const string WebmMimeType = "video/webm";

    private static readonly Regex WebmLinkRegex = new("https?[^ ]*.webm");

    private readonly ITelegramBotClient _bot;
    private readonly IAmazonSQS _sqsClient;
    private readonly ServicesSettings _servicesSettings;

    public MessageService(ITelegramBotClient bot, IAmazonSQS sqsClient, IOptions<ServicesSettings> servicesSettings)
    {
        _bot = bot;
        _sqsClient = sqsClient;
        _servicesSettings = servicesSettings.Value;
    }

    public async Task HandleAsync(Message message)
    {
        if (message.From?.IsBot == true)
        {
            return;
        }

        if (message.Text?.StartsWith("/start") == true)
        {
            await _bot.SendTextMessageAsync(new(message.Chat.Id),
                "Send me a video or link to WebM or add bot to group. 🇺🇦 Help the Ukrainian army fight russian and belarus invaders: https://savelife.in.ua/en/donate/");

            return;
        }

        if (message.Text?.Contains("!nsfw", StringComparison.InvariantCultureIgnoreCase) == false)
        {
            foreach (Match match in WebmLinkRegex.Matches(message.Text))
            {
                await SendMessageAsync(message, match.Value);
            }

            return;
        }

        if (message.Document?.MimeType?.EqualsCI(WebmMimeType) == true)
        {
            await SendMessageAsync(message);
        }
    }

    private async Task SendMessageAsync(Message receivedMessage, string link = null)
    {
        var sentMessage = await _bot.SendTextMessageAsync(new(receivedMessage.Chat.Id),
            "File is waiting to be downloaded 🕒",
            replyToMessageId: receivedMessage.MessageId,
            disableNotification: true);

        var downloaderMessage = new DownloaderMessage(receivedMessage, sentMessage, link);

        await _sqsClient.SendMessageAsync(new()
        {
            QueueUrl = _servicesSettings.DownloaderQueueUrl,
            MessageBody = JsonSerializer.Serialize(downloaderMessage, JsonSerializerConstants.SerializerOptions)
        });
    }
}