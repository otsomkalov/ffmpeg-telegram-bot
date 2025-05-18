namespace Telegram

open System.Threading.Tasks
open Domain.Core
open Telegram.Bot.Types
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

[<RequireQualifiedAccess>]
module Chat =
  type LoadResources = ChatId -> Task<IResourceProvider>

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

  [<RequireQualifiedAccess>]
  module UserConversion =
    type QueueProcessing = ChatMessageId -> ChatId -> BotMessageId -> Conversion.New.InputFile -> Task<unit>

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

type IExtendedBotService =
  abstract ReplyWithVideo: ChatMessageId * string * Conversion.Video * Conversion.Thumbnail -> Task<unit>

  inherit IBotService

type IFFMpegBot =
  abstract PrepareConversion: ConversionId * Conversion.New.InputFile -> Task<unit>
  abstract SaveVideo: ConversionId * ConversionResult -> Task<unit>
  abstract SaveThumbnail: ConversionId * ConversionResult -> Task<unit>
  abstract UploadConversion: ConversionId -> Task<unit>

type BuildExtendedBotService = ChatId -> IExtendedBotService