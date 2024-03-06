using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Database;

public class Translation
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; }
    public string Key { get; set; }
    public string Value { get; set; }
    public string Lang { get; set; }
}