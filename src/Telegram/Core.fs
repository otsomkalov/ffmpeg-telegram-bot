namespace Telegram

open System.Threading.Tasks
open Domain.Core
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums
open otsom.fs.Bot
open otsom.fs.Resources

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

  type ProcessPrivateMessage = Message -> Task<unit>
  type ProcessGroupMessage = Message -> Task<unit>
  type ProcessChannelPost = Message -> Task<unit>

  [<RequireQualifiedAccess>]
  type Command =
    | Start
    | Links of string seq
    | Document of string * string
    | Video of string * string

  type ParseCommand = Message -> Task<Command option>

open Core

open Core

type IExtendedBotService =
  abstract ReplyWithVideo: ChatMessageId * string * Conversion.Video * Conversion.Thumbnail -> Task<unit>

  inherit IBotService

type Update =
  | Msg of Message
  | Other of UpdateType

type IFFMpegBot =
  abstract ProcessUpdate: Update -> Task<unit>
  abstract PrepareConversion: ConversionId * Conversion.New.InputFile -> Task<unit>
  abstract SaveVideo: ConversionId * ConversionResult -> Task<unit>
  abstract SaveThumbnail: ConversionId * ConversionResult -> Task<unit>
  abstract UploadConversion: ConversionId -> Task<unit>

type BuildExtendedBotService = ChatId -> IExtendedBotService