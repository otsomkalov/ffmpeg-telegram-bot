using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using FFmpeg.NET;
using Serilog.Core;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using File = System.IO.File;

namespace WebMToMP4TelegramBot
{
    internal static class Program
    {
        private static TelegramBotClient _bot;
        private static readonly Engine FFMpeg = new Engine(@"/usr/local/bin/ffmpeg");
        private static Logger _logger;

        private static async Task Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("You need to supply bot token");

                return;
            }

            _logger = Configuration.ConfigureLogger();

            _bot = new TelegramBotClient(args[0]);

            _bot.OnMessage += OnMessageAsync;

            _bot.StartReceiving();

            _logger.Information("Bot started!");

            await Task.Delay(-1);
        }

        private static async void OnMessageAsync(object sender, MessageEventArgs messageEventArgs)
        {
            try
            {
                var message = messageEventArgs.Message;

                _logger.Information("Got message: {@Message}", message);

                if (message.Text?.StartsWith("/start") == true)
                {
                    await _bot.SendTextMessageAsync(
                        new ChatId(message.Chat.Id),
                        "Send me a video or link to WebM or add bot to group.");
                }
                else
                {
                    await ProcessMessageAsync(message);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error during processing message");
            }
        }

        private static async Task ProcessMessageAsync(Message message)
        {
            Message sentMessage;
            var inputFileName = $"{Path.GetTempPath()}{Guid.NewGuid()}.webm";

            if (message.Document != null)
            {
                if (!message.Document.FileName.Contains(".webm", StringComparison.InvariantCultureIgnoreCase)) return;

                sentMessage = await _bot.SendTextMessageAsync(
                    new ChatId(message.Chat.Id),
                    "Downloading file...",
                    replyToMessageId: message.MessageId);

                await using var fileStream = File.Create(inputFileName);
                await _bot.GetInfoAndDownloadFileAsync(message.Document.FileId, fileStream);
            }
            else
            {
                if (string.IsNullOrEmpty(message.Text)) return;
                if (!message.Text.Contains(".webm", StringComparison.InvariantCultureIgnoreCase)) return;
                if (!Uri.TryCreate(message.Text, UriKind.RelativeOrAbsolute, out var uri)) return;

                sentMessage = await _bot.SendTextMessageAsync(
                    new ChatId(message.Chat.Id),
                    "Downloading file...",
                    replyToMessageId: message.MessageId);

                using var webClient = new WebClient();

                await webClient.DownloadFileTaskAsync(uri, inputFileName);
            }

            sentMessage = await _bot.EditMessageTextAsync(
                new ChatId(sentMessage.Chat.Id),
                sentMessage.MessageId,
                "Conversion in progress...");

            var inputFile = new MediaFile(inputFileName);

            var outputFile = await FFMpeg.ConvertAsync(inputFile,
                new MediaFile($"{Path.GetTempPath()}{Guid.NewGuid().ToString()}.mp4"));

            sentMessage = await _bot.EditMessageTextAsync(
                new ChatId(sentMessage.Chat.Id),
                sentMessage.MessageId,
                "Generating thumbnail...");

            var thumbnail = await FFMpeg.GetThumbnailAsync(
                outputFile,
                new MediaFile($"{Path.GetTempPath()}{Guid.NewGuid()}.jpg"),
                new ConversionOptions {Seek = TimeSpan.Zero});

            await _bot.EditMessageTextAsync(
                new ChatId(sentMessage.Chat.Id),
                sentMessage.MessageId,
                "Uploading file to Telegram...");

            await using (var videoStream = File.OpenRead(outputFile.FileInfo.FullName))
            {
                await using var imageStream = File.OpenRead(thumbnail.FileInfo.FullName);

                await _bot.SendVideoAsync(
                    new ChatId(sentMessage.Chat.Id),
                    new InputMedia(videoStream, outputFile.FileInfo.Name),
                    replyToMessageId: message.MessageId,
                    thumb: new InputMedia(imageStream, thumbnail.FileInfo.Name));
            }

            await _bot.DeleteMessageAsync(
                new ChatId(sentMessage.Chat.Id),
                sentMessage.MessageId);

            File.Delete(inputFile.FileInfo.FullName);
            File.Delete(outputFile.FileInfo.FullName);
            File.Delete(thumbnail.FileInfo.FullName);
        }
    }
}
