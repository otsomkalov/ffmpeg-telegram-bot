﻿namespace Telegram

open System.Threading.Tasks
open Domain.Core
open Telegram.Bot.Types
open otsom.fs.Bot
open otsom.fs.Resources

module Core =
  type UserId =
    | UserId of int64

    member this.Value = let (UserId id) = this in id

  type ChannelId =
    | ChannelId of int64

    member this.Value = let (ChannelId id) = this in id

    static member Create id =
      if id < 0L then
        ChannelId id
      else
        failwith "ChannelId cannot be greater than 0"

  type GroupId =
    | GroupId of int64

    member this.Value = let (GroupId id) = this in id

    static member Create id =
      if id < 0L then
        GroupId id
      else
        failwith "GroupId cannot be greater than 0"

  type Group = { Id: GroupId; Banned: bool }

  type User =
    { Id: UserId
      Lang: string option
      Banned: bool }

  type Channel = { Id: ChannelId; Banned: bool }

  [<RequireQualifiedAccess>]
  type ConversionResult =
    | Success of name: string
    | Error of error: string

  type UserConversion =
    { ReceivedMessageId: ChatMessageId
      SentMessageId: BotMessageId
      ConversionId: ConversionId
      UserId: UserId option
      ChatId: ChatId }

  [<RequireQualifiedAccess>]
  module UserConversion =
    type QueueProcessing = ChatMessageId -> UserId option -> ChatId -> BotMessageId -> Conversion.New.InputFile -> Task<unit>

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

  [<RequireQualifiedAccess>]
  module Resources =
    type LoadResources = string option -> Task<IResourceProvider>

  [<RequireQualifiedAccess>]
  module User =
    type LoadResources = UserId option -> Task<IResourceProvider>

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