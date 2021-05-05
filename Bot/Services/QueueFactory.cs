using System;
using Azure.Storage.Queues;
using Bot.Settings;
using Microsoft.Extensions.Options;

namespace Bot.Services
{
    public enum Queue
    {
        Downloader,
        Converter,
        Uploader,
        Cleaner 
    }
    
    public interface IQueueFactory
    {
        QueueClient GetQueue(Queue queue);
    }
    
    public class QueueFactory : IQueueFactory
    {
        private readonly QueueClient _downloaderQueue;
        private readonly QueueClient _converterQueue;
        private readonly QueueClient _uploaderQueue;
        private readonly QueueClient _cleanerQueue;

        public QueueFactory(IOptions<ServicesSettings> servicesSettings)
        {
            var settings = servicesSettings.Value;
            
            _downloaderQueue = new (settings.ConnectionString, settings.DownloaderQueue);
            _converterQueue = new (settings.ConnectionString, settings.ConverterQueue);
            _uploaderQueue = new (settings.ConnectionString, settings.UploaderQueue);
            _cleanerQueue = new (settings.ConnectionString, settings.CleanerQueue);
        }
        
        public QueueClient GetQueue(Queue queue)
        {
            return queue switch
            {
                Queue.Downloader => _downloaderQueue,
                Queue.Converter => _converterQueue,
                Queue.Uploader => _uploaderQueue,
                Queue.Cleaner => _cleanerQueue,
                _ => throw new ArgumentOutOfRangeException(nameof(queue), queue, null)
            };
        }
    }
}