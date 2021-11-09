using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bot.Constants
{
    public static class JsonSerializerConstants
    {
        public static readonly JsonSerializerOptions SerializerOptions = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
    }
}
