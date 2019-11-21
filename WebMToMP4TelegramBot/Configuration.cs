using Serilog;
using Serilog.Core;
using Telegram.Bot.Types;

namespace WebMToMP4TelegramBot
{
    public static class Configuration
    {
        public static Logger ConfigureLogger()
        {
            return new LoggerConfiguration()
                .Destructure.ByTransforming<User>(GetUserTransformation)
                .Destructure.ByTransforming<Chat>(GetChatTransformation)
                .Destructure.ByTransforming<Message>(GetMessageTransformation)
                .Destructure.ByTransforming<InlineQuery>(GetInlineQueryTransformation)
                .WriteTo.File("./log/WebMToMP4TelegramBot.log")
                .WriteTo.Console()
                .CreateLogger();
        }

        private static object GetInlineQueryTransformation(InlineQuery inlineQuery)
        {
            return new
            {
                inlineQuery.Id,
                inlineQuery.From,
                inlineQuery.Query
            };
        }

        private static object GetChatTransformation(Chat chat)
        {
            return new
            {
                chat.Id,
                chat.Username
            };
        }

        private static object GetUserTransformation(User user)
        {
            return new
            {
                user.Id,
                user.Username
            };
        }

        private static object GetMessageTransformation(Message message)
        {
            return new
            {
                message.From,
                message.Chat,
                message.Text
            };
        }
    }
}
