namespace Bot.Models;

public enum DownloaderMessageType
{
    Link,
    Video,
    Document
}

public record DownloaderMessage(Message ReceivedMessage, Message SentMessage, string Link, DownloaderMessageType DownloaderMessageType);