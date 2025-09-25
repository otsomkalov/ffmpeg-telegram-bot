namespace Telegram

open System.Threading.Tasks
open Domain.Core
open otsom.fs.Bot

type Chat = {
  Id: ChatId
  Banned: bool
  Lang: string
}

type ICreateChat =
  abstract CreateChat: ChatId * string option -> Task<Chat>

type IChatSvc =
  inherit ICreateChat

module Core =
  [<RequireQualifiedAccess>]
  type ConversionResult =
    | Success of name: string
    | Error of error: string

  type UserConversion =
    { ReceivedMessageId: ChatMessageId
      SentMessageId: BotMessageId
      ConversionId: ConversionId
      ChatId: ChatId }

type IExtendedBotService =
  abstract ReplyWithVideo: ChatMessageId * string * Conversion.Video * Conversion.Thumbnail -> Task<unit>

  inherit IBotService

type Doc =
  { Id: string
    Name: string
    Caption: string option
    MimeType: string }

type Vid =
  { Id: string
    Name: string option
    Caption: string option
    MimeType: string }

type UserMsg =
  {
    ChatId: ChatId
    MessageId: ChatMessageId
    Lang: string option
    Text: string option
    Doc: Doc option
    Vid: Vid option }

type Msg =
  | UserMsg of UserMsg
  | BotMsg

type Update =
  | Msg of Msg
  | Other of string

open Core

type IFFMpegBot =
  abstract ProcessUpdate: Update -> Task<unit>
  abstract PrepareConversion: ConversionId * Conversion.New.InputFile -> Task<unit>
  abstract SaveVideo: ConversionId * ConversionResult -> Task<unit>
  abstract SaveThumbnail: ConversionId * ConversionResult -> Task<unit>
  abstract UploadConversion: ConversionId -> Task<unit>

type BuildExtendedBotService = ChatId -> IExtendedBotService