using Telegram.Bot.Types;

namespace Bot.Models
{
    public record ConverterMessage(Message ReceivedMessage, Message SentMessage, string InputFilePath,string LinkOrFileName)
    {
    }
}