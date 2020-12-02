using System;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FFmpeg.NET;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;
using File = System.IO.File;
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
        private readonly ILogger _logger;
        private readonly Engine _engine;
        private static readonly Regex WebmRegex = new Regex("https?[^ ]*.webm");

        public MessageService(ITelegramBotClient bot, ILogger<MessageService> logger, Engine engine)
        {
            _bot = bot;
            _logger = logger;
            _engine = engine;
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
                    new ChatId(message.Chat.Id),
                    "Send me a video or link to WebM or add bot to group.");
            }
            else
            {
                ProcessMessageAsync(message);
            }
        }
        
        private void ProcessMessageAsync(Message message)
        {
            if (message?.Document?.FileName?.EndsWith(".webm", StringComparison.InvariantCultureIgnoreCase) == true)
            {
                if (message.Caption?.Contains("!nsfw", StringComparison.InvariantCultureIgnoreCase) != true)
                {
                    HandleDocumentAsync(message);
                }
            }

            if (!string.IsNullOrEmpty(message?.Text))
            {
                if (message.Text.Contains("!nsfw", StringComparison.InvariantCultureIgnoreCase) != true)
                {
                    var matches = WebmRegex.Matches(message.Text);

                    foreach (Match match in matches)
                    {
                        HandleLinkAsync(message, match.Value);
                    }   
                }
            }

            if (!string.IsNullOrEmpty(message?.Caption))
            {
                if (message.Caption.Contains("!nsfw", StringComparison.InvariantCultureIgnoreCase) != true)
                {
                    var matches = WebmRegex.Matches(message.Caption);

                    foreach (Match match in matches)
                    {
                        HandleLinkAsync(message, match.Value);
                    }
                }
            }
        }

        private async void HandleLinkAsync(Message receivedMessage, string link)
        {
            var inputFileName = $"{Path.GetTempPath()}{Guid.NewGuid()}.webm";

            var sentMessage = await _bot.SendTextMessageAsync(
                new ChatId(receivedMessage.Chat.Id),
                $"{link}\nDownloading file 📥",
                replyToMessageId: receivedMessage.MessageId,
                disableNotification: true);

            using var webClient = new WebClient();

            try
            {
                await webClient.DownloadFileTaskAsync(link, inputFileName);

                await ProcessFileAsync(receivedMessage, sentMessage, inputFileName, link);
            }
            catch (WebException webException)
            {
                if (webException.Response is HttpWebResponse response)
                {
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.Unauthorized:

                            await _bot.EditMessageTextAsync(
                                new ChatId(sentMessage.Chat.Id),
                                sentMessage.MessageId,
                                $"{link}\nI am not authorized to download video from this source 🚫");

                            return;

                        case HttpStatusCode.NotFound:

                            await _bot.EditMessageTextAsync(
                                new ChatId(sentMessage.Chat.Id),
                                sentMessage.MessageId,
                                $"{link}\nVideo not found ⚠️");

                            return;

                        case HttpStatusCode.InternalServerError:

                            await _bot.EditMessageTextAsync(
                                new ChatId(sentMessage.Chat.Id),
                                sentMessage.MessageId,
                                $"{link}\nServer error 🛑");

                            return;
                    }
                }
            }
        }

        private async void HandleDocumentAsync(Message receivedMessage)
        {
            var inputFileName = $"{Path.GetTempPath()}{Guid.NewGuid()}.webm";

            var sentMessage = await _bot.SendTextMessageAsync(
                new ChatId(receivedMessage.Chat.Id), 
                $"{receivedMessage.Document.FileName}\nDownloading file 📥",
                replyToMessageId: receivedMessage.MessageId,
                disableNotification: true);

            await using (var fileStream = File.Create(inputFileName))
            {
                await _bot.GetInfoAndDownloadFileAsync(receivedMessage.Document.FileId, fileStream);
            }

            await ProcessFileAsync(receivedMessage, sentMessage, inputFileName, receivedMessage.Document.FileName);
        }

        private async Task ProcessFileAsync(Message receivedMessage, Message sentMessage, string inputFileName,
            string link)
        {
            _ = _bot.EditMessageTextAsync(
                new ChatId(sentMessage.Chat.Id),
                sentMessage.MessageId,
                $"{link}\nConversion in progress 🚀");
            
            var inputFile = new MediaFile(inputFileName);

            MediaFile outputFile;
            
            try
            {
                outputFile = await _engine.ConvertAsync(inputFile,
                    new MediaFile($"{Path.GetTempPath()}{Guid.NewGuid().ToString()}.mp4"));
            }
            catch (Exception)
            {
                _ = _bot.EditMessageTextAsync(
                    new ChatId(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{link}\nError during file conversion");
                
                CleanupFiles(inputFile);

                throw;
            }

            _ = _bot.EditMessageTextAsync(
                new ChatId(sentMessage.Chat.Id),
                sentMessage.MessageId,
                $"{link}\nGenerating thumbnail 🖼️");

            MediaFile thumbnail;
            
            try
            {
                thumbnail = await _engine.GetThumbnailAsync(
                    outputFile,
                    new MediaFile($"{Path.GetTempPath()}{Guid.NewGuid()}.jpg"),
                    new ConversionOptions {Seek = TimeSpan.Zero});
            }
            catch (Exception)
            {
                _ = _bot.EditMessageTextAsync(
                    new ChatId(sentMessage.Chat.Id),
                    sentMessage.MessageId,
                    $"{link}\nError during file conversion");
                
                CleanupFiles(inputFile, outputFile);

                throw;
            }

            _ = _bot.EditMessageTextAsync(
                new ChatId(sentMessage.Chat.Id),
                sentMessage.MessageId, 
                $"{link}\nUploading file to Telegram 📤");

            await using (var videoStream = File.OpenRead(outputFile.FileInfo.FullName))
            {
                await using var imageStream = File.OpenRead(thumbnail.FileInfo.FullName);

                try
                {
                    _ = _bot.DeleteMessageAsync(
                        new ChatId(sentMessage.Chat.Id),
                        sentMessage.MessageId
                    );

                    await _bot.SendVideoAsync(
                        new ChatId(sentMessage.Chat.Id),
                        new InputMedia(videoStream, outputFile.FileInfo.Name),
                        replyToMessageId: receivedMessage.MessageId,
                        thumb: new InputMedia(imageStream, thumbnail.FileInfo.Name),
                        caption: link,
                        disableNotification: true);
                }
                catch (Exception)
                {
                    _ = _bot.EditMessageTextAsync(
                        new ChatId(sentMessage.Chat.Id),
                        sentMessage.MessageId,
                        $"{link}\nError during file upload");
                    
                    CleanupFiles(inputFile, outputFile, thumbnail);

                    throw;
                }
            }

            CleanupFiles(inputFile, outputFile, thumbnail);
        }

        private static void CleanupFiles(MediaFile inputFile = null, MediaFile outputFile = null, MediaFile thumbnail = null)
        {
            if (inputFile?.FileInfo?.FullName != null)
            {
                File.Delete(inputFile.FileInfo.FullName);
            }
            
            if (outputFile?.FileInfo?.FullName != null)
            {
                File.Delete(outputFile.FileInfo.FullName);
            }
            
            if (thumbnail?.FileInfo?.FullName != null)
            {
                File.Delete(thumbnail.FileInfo.FullName);
            }
        }
    }
}