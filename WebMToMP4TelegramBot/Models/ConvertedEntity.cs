namespace WebMToMP4TelegramBot.Models
{
    public class ConvertedEntity
    {
        public long ChatId { get; set; }

        public int SendedMessageId { get; set; }

        public string OutputFileName { get; set; }

        public int ReceivedMessageId { get; set; }
    }
}
