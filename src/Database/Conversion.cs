namespace Database;

public enum ConversionState
{
    New,
    Prepared,
    Converted,
    Thumbnailed,
    Completed
}

public class Conversion
{
    public string Id { get; set; }

    public string InputFileName { get; set; }

    public string OutputFileName { get; set; }

    public string ThumbnailFileName { get; set; }

    public long UserId { get; set; }

    public long ChatId { get; set; }

    public int ReceivedMessageId { get; set; }

    public int SentMessageId { get; set; }

    public ConversionState State { get; set; }
}