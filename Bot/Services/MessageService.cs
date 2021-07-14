using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Amazon.SQS;
using Bot.Constants;
using Bot.Models;
using Bot.Settings;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bot.Services
{
    public class MessageService
    {
        private readonly ITelegramBotClient _bot;
        private readonly IAmazonSQS _sqsClient;
        private readonly ServicesSettings _servicesSettings;
        private static readonly Regex WebmRegex = new("[^ ]*.webm");
        private static readonly Regex WebmLinkRegex = new("https?[^ ]*.webm");

        public MessageService(ITelegramBotClient bot, IAmazonSQS sqsClient, IOptions<ServicesSettings> servicesSettings)
        {
            _bot = bot;
            _sqsClient = sqsClient;
            _servicesSettings = servicesSettings.Value;
        }

        public async Task HandleAsync(Message message)
        {
            if (message.Text?.StartsWith("/start") == true)
            {
                await _bot.SendTextMessageAsync(new(message.Chat.Id),
                    "Send me a video or link to WebM or add bot to group.");
            }
            else
            {
                await ProcessMessageAsync(message);
            }
        }

        private async Task ProcessMessageAsync(Message message)
        {
            if (Nsfw(message.Text) || Nsfw(message.Caption))
            {
                return;
            }

            if (!string.IsNullOrEmpty(message.Document?.FileName) && WebmRegex.IsMatch(message.Document.FileName))
            {
                await SendMessageAsync(message);
            }

            if (message.Entities == null)
            {
                return;
            }

            foreach (var messageEntity in message.Entities.Where(e => e.Type == MessageEntityType.Url))
            {
                if (WebmLinkRegex.IsMatch(messageEntity.Url))
                {
                    await SendMessageAsync(message, messageEntity.Url);
                }
            }
        }

        private static bool Nsfw(string text)
        {
            return !string.IsNullOrEmpty(text) && text.StartsWith("!nsfw", StringComparison.InvariantCultureIgnoreCase);
        }

        private async Task SendMessageAsync(Message receivedMessage, string link = null)
        {
            var sentMessage = await _bot.SendTextMessageAsync(new(receivedMessage.Chat.Id),
                "File is waiting to be downloaded 🕒",
                replyToMessageId: receivedMessage.MessageId,
                disableNotification: true);

            var downloaderMessage = new DownloaderMessage(receivedMessage, sentMessage, link);

            await _sqsClient.SendMessageAsync(_servicesSettings.DownloaderQueueUrl,
                JsonSerializer.Serialize(downloaderMessage, JsonSerializerConstants.SerializerOptions));
        }
    }
}
