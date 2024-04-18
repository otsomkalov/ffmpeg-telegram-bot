using MongoDB.Bson.Serialization.Attributes;

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
    [BsonId]
    public string Id { get; set; }

    public string InputFileName { get; set; }

    public string OutputFileName { get; set; }

    public string ThumbnailFileName { get; set; }

    public long? UserId { get; set; }

    public long ChatId { get; set; }

    public int ReceivedMessageId { get; set; }

    public int SentMessageId { get; set; }

    public ConversionState State { get; set; }

    [BsonElement]
    public DateTime CreatedAt { get; } = DateTime.Now;
}