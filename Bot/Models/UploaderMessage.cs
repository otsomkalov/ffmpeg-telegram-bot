namespace Bot.Models;

public record UploaderMessage(Message ReceivedMessage, Message SentMessage, string InputFilePath, string OutputFilePath,
    string ThumbnailFilePath);