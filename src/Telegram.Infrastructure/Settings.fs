namespace Telegram.Infrastructure

module Settings =
  [<CLIMutable>]
  type TelegramSettings =
    { Token: string
      ApiUrl: string }

    static member SectionName = "Telegram"