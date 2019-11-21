using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FFmpeg.NET;
using FFmpeg.NET.Events;
using Serilog.Core;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Types;
using WebMToMP4TelegramBot.Models;
using File = System.IO.File;

namespace WebMToMP4TelegramBot
{
    internal class Program
    {
        private static TelegramBotClient _bot;
        private static readonly Engine _ffmpeg = new Engine(@"/usr/local/bin/ffmpeg");
        private static readonly WebClient _webClient = new WebClient();
        private static readonly List<ConvertedEntity> _entities = new List<ConvertedEntity>();
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

            _ffmpeg.Complete += OnConversionCompletedAsync;

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

                await ProcessMessageAsync(message);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Error during processing message");
            }
        }

        private static async Task ProcessMessageAsync(Message receivedMessage)
        {
            if (receivedMessage.Document != null)
            {
                if (!receivedMessage.Document.FileName.Contains(".webm",
                    StringComparison.InvariantCultureIgnoreCase)) return;

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
                if (!receivedMessage.Text.Contains(".webm", StringComparison.InvariantCultureIgnoreCase)) return;
                if (!Uri.TryCreate(receivedMessage.Text, UriKind.RelativeOrAbsolute, out var uri)) return;

                var sentMessage = await _bot.SendTextMessageAsync(
                    new ChatId(receivedMessage.Chat.Id),
                    "Downloading file...",
                    replyToMessageId: receivedMessage.MessageId);

                await _webClient.DownloadFileTaskAsync(uri, uri.Segments.Last());


                await _bot.EditMessageTextAsync(
                    new ChatId(receivedMessage.Chat.Id),
                    sentMessage.MessageId,
                    "Conversion in progress...");

                var inputFile = new MediaFile(uri.Segments.Last());

                var outputFile = new MediaFile($"{Guid.NewGuid().ToString()}.mp4");

                _entities.Add(new ConvertedEntity
                {
                    ChatId = receivedMessage.Chat.Id,
                    SendedMessageId = sentMessage.MessageId,
                    OutputFileName = outputFile.FileInfo.Name,
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
