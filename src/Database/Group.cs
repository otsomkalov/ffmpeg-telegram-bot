using MongoDB.Bson.Serialization.Attributes;

namespace Database;

[BsonIgnoreExtraElements]
public class Group
{
    public long Id { get; set; }

    public bool Banned { get; set; }
}