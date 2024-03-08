[<RequireQualifiedAccess>]
module Bot.Settings

[<CLIMutable>]
type StorageSettings = { Queue: string }

[<CLIMutable>]
type ConverterSettings' = { Queue: string; Container: string }

[<CLIMutable>]
type ConverterSettings =
  { Input: ConverterSettings'
    Output: ConverterSettings' }

[<CLIMutable>]
type WorkersSettings =
  { ConnectionString: string
    Downloader: StorageSettings
    Converter: ConverterSettings
    Thumbnailer: ConverterSettings
    Uploader: StorageSettings }

  static member SectionName = "Workers"

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
