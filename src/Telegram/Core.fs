namespace Telegram

open System.Threading.Tasks
open Domain.Core
open otsom.fs.Telegram.Bot.Core

module Core =
  type ChatId = ChatId of int64
  type UserMessageId = UserMessageId of int
  type UploadCompletedConversion = ConversionId -> Task<unit>

  [<RequireQualifiedAccess>]
  type ConversionResult =
    | Success of name: string
    | Error of error: string

  type ProcessThumbnailingResult = ConversionId -> ConversionResult -> Task<unit>
  type ProcessConversionResult = ConversionId -> ConversionResult -> Task<unit>

  type UserConversion =
    { ReceivedMessageId: UserMessageId
      SentMessageId: BotMessageId
      ConversionId: string
      UserId: UserId option
      ChatId: UserId }

  type Translation = { Key: string; Value: string }

  module Translation =
    [<Literal>]
    let DefaultLang = "en"

  type GetTranslation = string -> string
  type FormatTranslation = string * obj array -> string

  type GetLocaleTranslations = string option -> Task<GetTranslation * FormatTranslation>

