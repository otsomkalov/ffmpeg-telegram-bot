﻿namespace Telegram

open System.Threading.Tasks
open Domain.Core
open Domain.Repos
open Telegram.Bot.Types
open otsom.fs.Telegram.Bot.Core

module Core =
  type ChatId = ChatId of int64

  type ChannelId = ChannelId of int64

  [<RequireQualifiedAccess>]
  module ChannelId =
    let create id =
      if id < 0L then
        ChannelId id
      else
        failwith "ChannelId cannot be greater than 0"

    let value (ChannelId id) = id

  type GroupId = GroupId of int64

  [<RequireQualifiedAccess>]
  module GroupId =
    let create id =
      if id < 0L then
        GroupId id
      else
        failwith "GroupId cannot be greater than 0"

    let value (GroupId id) = id

  type Group = { Id: GroupId; Banned: bool }

  type UserMessageId = UserMessageId of int
  type UploadCompletedConversion = ConversionId -> Task<unit>

  type User = { Id: UserId; Lang: string option; Banned: bool }

  type Channel = { Id: ChannelId; Banned: bool }


  [<RequireQualifiedAccess>]
  type ConversionResult =
    | Success of name: string
    | Error of error: string

  type DownloadFileAndQueueConversion = ConversionId -> Conversion.New.InputFile -> Task<unit>

  type ProcessThumbnailingResult = ConversionId -> ConversionResult -> Task<unit>
  type ProcessConversionResult = ConversionId -> ConversionResult -> Task<unit>

  type UserConversion =
    { ReceivedMessageId: UserMessageId
      SentMessageId: BotMessageId
      ConversionId: ConversionId
      UserId: UserId option
      ChatId: UserId }

  [<RequireQualifiedAccess>]
  module UserConversion =
    type QueueProcessing = UserMessageId -> UserId option -> UserId -> BotMessageId -> Conversion.New.InputFile -> Task<unit>

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