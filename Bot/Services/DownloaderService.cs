using System;
using System.IO;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Bot.Models;
using Bot.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Types;
using File = System.IO.File;
using Message = Telegram.Bot.Types.Message;

namespace Bot.Services
{
    public class DownloaderService : BackgroundService
    {
        private readonly QueueClient _converterQueue;
        private readonly QueueClient _downloaderQueue;
        private readonly ITelegramBotClient _bot;
        private readonly ILogger<DownloaderService> _logger;
        private readonly ServicesSettings _servicesSettings;

        public DownloaderService(IQueueFactory queueFactory, ITelegramBotClient bot, ILogger<DownloaderService> logger,
            IOptions<ServicesSettings> servicesSettings)
        {
            _bot = bot;
            _logger = logger;
            _converterQueue = queueFactory.GetQueue(Queue.Converter);
            _downloaderQueue = queueFactory.GetQueue(Queue.Downloader);
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
                
                await Task.Delay(_servicesSettings.ProcessingDelay, stoppingToken);
            }
        }

        private async Task RunAsync(CancellationToken stoppingToken)
        {
            var response = await _downloaderQueue.ReceiveMessageAsync(cancellationToken: stoppingToken);

            if (response.Value is not {Body: null} queueMessage) return;
            
            var (receivedMessage, sentMessage, linkOrFileName) = JsonSerializer.Deserialize<DownloaderMessage>(queueMessage.Body)!;
            
            await _bot.EditMessageTextAsync(
                new(sentMessage.Chat.Id),
                sentMessage.MessageId,
                $"{linkOrFileName}\nDownloading file 🚀",
                cancellationToken: stoppingToken);
            
            var inputFilePath = $"{Path.GetTempPath()}{Guid.NewGuid()}.webm";
            
            if (string.IsNullOrEmpty(linkOrFileName))
            {
                await HandleDocumentAsync(receivedMessage, sentMessage, inputFilePath);
            }
            else
            {
                await HandleLinkAsync(receivedMessage, sentMessage, linkOrFileName, inputFilePath);
            }
            
            await _downloaderQueue.DeleteMessageAsync(queueMessage.MessageId, queueMessage.PopReceipt, stoppingToken);
            
            await _bot.EditMessageTextAsync(
                new(sentMessage.Chat.Id),
                sentMessage.MessageId,
                $"{linkOrFileName}\nYour file is waiting to be converted 🕒",
                cancellationToken: stoppingToken);
        }

        private async Task HandleLinkAsync(Message receivedMessage, Message sentMessage, string linkOrFileName, string inputFilePath)
        {
            using var webClient = new WebClient();

            try
            {
                await webClient.DownloadFileTaskAsync(linkOrFileName, inputFilePath);

                await SendMessageAsync(receivedMessage, sentMessage, inputFilePath,
                    linkOrFileName);
            }
            catch (WebException webException)
            {
                if (webException.Response is HttpWebResponse response)
                {
                    switch (response.StatusCode)
                    {
                        case HttpStatusCode.Unauthorized:

                            await _bot.EditMessageTextAsync(
                                new(sentMessage.Chat.Id),
                                sentMessage.MessageId,
                                $"{linkOrFileName}\nI am not authorized to download video from this source 🚫");

                            return;

                        case HttpStatusCode.NotFound:

                            await _bot.EditMessageTextAsync(
                                new(sentMessage.Chat.Id),
                                sentMessage.MessageId,
                                $"{linkOrFileName}\nVideo not found ⚠️");

                            return;

                        case HttpStatusCode.InternalServerError:

                            await _bot.EditMessageTextAsync(
                                new(sentMessage.Chat.Id),
                                sentMessage.MessageId,
                                $"{linkOrFileName}\nServer error 🛑");

                            return;
                    }
                }
            }
        }

        private async Task HandleDocumentAsync(Message receivedMessage, Message sentMessage, string inputFileName)
        {
            await using (var fileStream = File.Create(inputFileName))
            {
                await _bot.GetInfoAndDownloadFileAsync(receivedMessage.Document.FileId, fileStream);
            }

            await SendMessageAsync(receivedMessage, sentMessage, inputFileName, receivedMessage.Document.FileName);
        }

        private async Task SendMessageAsync(Message receivedMessage, Message sentMessage, string inputFilePath,
            string linkOrFilename)
        {
            var converterMessage = new ConverterMessage(receivedMessage, sentMessage, inputFilePath, linkOrFilename);

            await _converterQueue.SendMessageAsync(JsonSerializer.Serialize(converterMessage));
        }
    }
}