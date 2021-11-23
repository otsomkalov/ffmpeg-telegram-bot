namespace Bot.Models;

public record DownloaderMessage(Message ReceivedMessage, Message SentMessage, string Link);