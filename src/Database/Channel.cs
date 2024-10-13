using MongoDB.Bson.Serialization.Attributes;

namespace Database;

[BsonIgnoreExtraElements]
public class Channel
{
    public long Id { get; set; }

    public bool Banned { get; set; }
}