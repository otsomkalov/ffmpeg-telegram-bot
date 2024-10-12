using MongoDB.Bson.Serialization.Attributes;

namespace Database;

[BsonIgnoreExtraElements]
public class User
{
    public long Id { get; set; }

    public string Lang { get; set; }
}