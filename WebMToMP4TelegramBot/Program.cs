using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FFmpeg.NET;
using FFmpeg.NET.Events;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using File = System.IO.File;

namespace WebMToMP4TelegramBot
{
    internal static class Program
    {
        private static TelegramBotClient _bot;
        private static Engine _ffmpeg;
        private static WebClient _webClient;
        private static List<ConvertedEntity> _entities;

        private static async Task Main(string[] args)
        {
            if (args.Length != 1)
            {
                Console.WriteLine("You need to supply bot token");

                return;
            }

            _bot = new TelegramBotClient(args[0]);

            _bot.OnMessage += OnMessageAsync;

            _bot.StartReceiving();
            _ffmpeg = new Engine(@"/usr/local/bin/ffmpeg"); //@"C:\Program Files\ffmpeg\bin\ffmpeg.exe");
            _ffmpeg.Complete += OnConversionCompletedAsync;
            _webClient = new WebClient();
            _entities = new List<ConvertedEntity>();

            Console.WriteLine("Bot started!");

            while (true)
            {
                await Task.Delay(int.MaxValue);
            }
        }

        private static async void OnMessageAsync(object sender, MessageEventArgs messageEventArgs)
        {
            var message = messageEventArgs.Message;

            try
            {
                await ProcessMessageAsync(message);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        private static async Task ProcessMessageAsync(Message receivedMessage)
        {
            if (receivedMessage.Document != null)
            {
                if (!receivedMessage.Document.FileName.Contains(".webm", StringComparison.InvariantCultureIgnoreCase)) return;

                var msg = await _bot.SendTextMessageAsync(
                    new ChatId(receivedMessage.Chat.Id),
                    "Downloading file...",
                    replyToMessageId: receivedMessage.MessageId);

                await using (var fileStream = File.OpenWrite(receivedMessage.Document.FileName))
                {
                    await _bot.GetInfoAndDownloadFileAsync(receivedMessage.Document.FileId, fileStream);
                }

                var outputFileName = $"{Guid.NewGuid().ToString()}.mp4";

                await _bot.EditMessageTextAsync(
                    new ChatId(receivedMessage.Chat.Id),
                    msg.MessageId,
                    "Conversion in progress...");

                var inputFile = new MediaFile(receivedMessage.Document.FileName);

                var outputFile = new MediaFile(outputFileName);

                _entities.Add(new ConvertedEntity
                {
                    ChatId = receivedMessage.Chat.Id,
                    SendedMessageId = msg.MessageId,
                    OutputFileName = outputFileName,
                    ReceivedMessageId = receivedMessage.MessageId
                });

                await _ffmpeg.ConvertAsync(inputFile, outputFile);
            }
            else
            {
                if (receivedMessage.Text == null) return;
                if (!Uri.TryCreate(receivedMessage.Text, UriKind.RelativeOrAbsolute, out var uri)) return;
                if (!uri.ToString().Contains(".webm", StringComparison.InvariantCultureIgnoreCase)) return;

                var sentMessage = await _bot.SendTextMessageAsync(
                    new ChatId(receivedMessage.Chat.Id),
                    "Downloading file...",
                    replyToMessageId: receivedMessage.MessageId);

                await _webClient.DownloadFileTaskAsync(uri, uri.Segments.Last());

                var outputFileName = $"{Guid.NewGuid().ToString()}.mp4";

                await _bot.EditMessageTextAsync(
                    new ChatId(receivedMessage.Chat.Id),
                    sentMessage.MessageId,
                    "Conversion in progress...");

                var inputFile = new MediaFile(uri.Segments.Last());

                var outputFile = new MediaFile(outputFileName);

                _entities.Add(new ConvertedEntity
                {
                    ChatId = receivedMessage.Chat.Id,
                    SendedMessageId = sentMessage.MessageId,
                    OutputFileName = outputFileName,
                    ReceivedMessageId = receivedMessage.MessageId
                });

                await _ffmpeg.ConvertAsync(inputFile, outputFile);
            }
        }

        private static async void OnConversionCompletedAsync(object sender, ConversionCompleteEventArgs eventArgs)
        {
            var convertedEntity = _entities.SingleOrDefault(entity =>
                entity.OutputFileName == eventArgs.Output.FileInfo.Name);

            if (convertedEntity == null) return;
            
            await _bot.EditMessageTextAsync(
                new ChatId(convertedEntity.ChatId),
                convertedEntity.SendedMessageId,
                "Generating thumbnail...");

            var thumbnail = await _ffmpeg.GetThumbnailAsync(
                eventArgs.Input,
                new MediaFile($"{Guid.NewGuid()}.jpg"),
                new ConversionOptions {Seek = TimeSpan.Zero});

            await _bot.EditMessageTextAsync(
                new ChatId(convertedEntity.ChatId),
                convertedEntity.SendedMessageId,
                "Uploading file to Telegram...");

            await using (var videoStream = File.OpenRead(eventArgs.Output.FileInfo.FullName))
            {
                await using var imageStream = File.OpenRead(thumbnail.FileInfo.FullName);
                    
                await _bot.SendVideoAsync(
                    new ChatId(convertedEntity.ChatId),
                    new InputMedia(videoStream, eventArgs.Output.FileInfo.Name),
                    replyToMessageId: convertedEntity.ReceivedMessageId,
                    thumb: new InputMedia(imageStream, thumbnail.FileInfo.Name));
            }

            await _bot.DeleteMessageAsync(new ChatId(convertedEntity.ChatId), convertedEntity.SendedMessageId);

            File.Delete(eventArgs.Input.FileInfo.FullName);
            File.Delete(eventArgs.Output.FileInfo.FullName);
            File.Delete(thumbnail.FileInfo.FullName);

            _entities.Remove(convertedEntity);
        }
    }
}