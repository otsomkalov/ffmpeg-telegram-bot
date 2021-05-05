using System;

namespace Bot.Settings
{
    public class ServicesSettings
    {
        public const string SectionName = "Services";

        public string DownloaderQueue { get; set; }

        public string ConverterQueue { get; set; }

        public string UploaderQueue { get; set; }

        public string CleanerQueue { get; set; }

        public string ConnectionString { get; set; }

        public TimeSpan ProcessingDelay { get; set; }
    }
}
