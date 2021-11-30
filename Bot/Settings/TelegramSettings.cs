namespace Bot.Settings;

public class TelegramSettings
{
    public const string SectionName = "Telegram";

    public string ApiUrl { get; set; }

    public string Token { get; set; }
}