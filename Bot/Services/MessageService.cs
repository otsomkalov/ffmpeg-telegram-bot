using System;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Bot.Models;
using Telegram.Bot;
using Telegram.Bot.Types;
using Message = Telegram.Bot.Types.Message;

namespace Bot.Services
{
    public interface IMessageService
    {
        Task HandleAsync(Message message);
    }
    
    public class MessageService : IMessageService
    {
        private readonly ITelegramBotClient _bot;
        private readonly QueueClient _downloaderQueue;
        private static readonly Regex WebmRegex = new("[^ ]*.webm");
        private static readonly Regex WebmLinkRegex = new("https?[^ ]*.webm");

        public MessageService(ITelegramBotClient bot, IQueueFactory queueFactory)
        {
            _bot = bot;
            _downloaderQueue = queueFactory.GetQueue(Queue.Downloader);
        }

        public async Task HandleAsync(Message message)
        {
            if (message.From?.IsBot == true)
            {
                return;
            }

            if (message.Text?.StartsWith("/start") == true)
            {
                await _bot.SendTextMessageAsync(
                    new(message.Chat.Id),
                    "Send me a video or link to WebM or add bot to group.");
            }
            else
            {
                await ProcessMessageAsync(message);
            }
        }
        
        private async Task ProcessMessageAsync(Message message)
        {
            if (message.Document != null && WebmRegex.IsMatch(message.Document.FileName) &&
                (string.IsNullOrEmpty(message.Caption) || !Nsfw(message.Caption)))
            {
                await SendMessageAsync(message);
            }
            
            if (!string.IsNullOrEmpty(message.Caption) && !Nsfw(message.Caption))
            {
                var matches = WebmLinkRegex.Matches(message.Caption);

                foreach (Match match in matches)
                {
                    await SendMessageAsync(message, match.Value);
                }
            }
            
            if (!string.IsNullOrEmpty(message.Text) && !Nsfw(message.Text))
            {
                var matches = WebmLinkRegex.Matches(message.Text);

                foreach (Match match in matches)
                {
                    await SendMessageAsync(message, match.Value);
                }
            }
        }

        private static bool Nsfw(string text)
        {
            return text != null && text.StartsWith("!nsfw", StringComparison.InvariantCultureIgnoreCase);
        }

        private async Task SendMessageAsync(Message receivedMessage, string linkOrFileName = null)
        {
            var sentMessage = await _bot.SendTextMessageAsync(
                new(receivedMessage.Chat.Id),
                    $"{linkOrFileName}\nFile is waiting to be downloaded 🕒",
                replyToMessageId: receivedMessage.MessageId,
                disableNotification: true);

            var downloaderMessage = new DownloaderMessage(receivedMessage, sentMessage, linkOrFileName);

            await _downloaderQueue.SendMessageAsync(JsonSerializer.Serialize(downloaderMessage));
        }
    }
}