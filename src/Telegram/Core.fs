namespace Telegram

open System.Threading.Tasks
open Domain.Core
open Telegram.Bot.Types
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

  type ProcessPrivateMessage = Message -> Task<unit>
  type ProcessGroupMessage = Message -> Task<unit>
  type ProcessChannelPost = Message -> Task<unit>

open Core

type IExtendedBotService =
  abstract ReplyWithVideo: ChatMessageId * string * Conversion.Video * Conversion.Thumbnail -> Task<unit>

  inherit IBotService

type IFFMpegBot =
  abstract ProcessMessage: Message -> Task<unit>
  abstract PrepareConversion: ConversionId * Conversion.New.InputFile -> Task<unit>
  abstract SaveVideo: ConversionId * ConversionResult -> Task<unit>
  abstract SaveThumbnail: ConversionId * ConversionResult -> Task<unit>
  abstract UploadConversion: ConversionId -> Task<unit>

type BuildExtendedBotService = ChatId -> IExtendedBotService