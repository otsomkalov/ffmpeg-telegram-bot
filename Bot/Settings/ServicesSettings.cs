namespace Bot.Settings;

public class ServicesSettings
{
    public const string SectionName = "Services";

    public string DownloaderQueueUrl { get; set; }

    public string ConverterQueueUrl { get; set; }

    public string UploaderQueueUrl { get; set; }

    public string CleanerQueueUrl { get; set; }
}