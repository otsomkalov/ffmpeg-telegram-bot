using System.Text.Json;

namespace Bot.Constants
{
    public static class JsonSerializerConstants
    {
        public static readonly JsonSerializerOptions SerializerOptions = new()
        {
            IgnoreNullValues = true
        };
    }
}
