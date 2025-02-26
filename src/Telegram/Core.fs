namespace Telegram

open System.Threading.Tasks
open Domain.Core
open Domain.Repos
open Telegram.Bot.Types
open otsom.fs.Telegram.Bot.Core

module Core =
  type UserId with
    member this.Value = let (UserId id) = this in id

  type ChatId = ChatId of int64

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

  type UserMessageId =
    | UserMessageId of int

    member this.Value = let (UserMessageId id) = this in id

  type UploadCompletedConversion = ConversionId -> Task<unit>

  type User =
    { Id: UserId
      Lang: string option
      Banned: bool }

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

  type Translation = { Key: string; Value: string }

  [<RequireQualifiedAccess>]
  module Translation =
    [<Literal>]
    let DefaultLang = "en"

    type GetTranslation = string -> string
    type FormatTranslation = string * obj array -> string

    type LoadTranslations = string option -> Task<GetTranslation * FormatTranslation>
    type LoadDefaultTranslations = unit -> Task<GetTranslation * FormatTranslation>

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
  module User =
    type LoadTranslations = UserId option -> Task<Translation.GetTranslation * Translation.FormatTranslation>