[<RequireQualifiedAccess>]
module Bot.Settings

[<CLIMutable>]
type DatabaseSettings =
  { ConnectionString: string
    Name: string }

  static member SectionName = "Database"

[<CLIMutable>]
type TelegramSettings =
  { Token: string
    ApiUrl: string }

  static member SectionName = "Telegram"

[<CLIMutable>]
type InputValidationSettings =
  { LinkRegex: string
    MimeTypes: string seq }

  static member SectionName = "Validation"
