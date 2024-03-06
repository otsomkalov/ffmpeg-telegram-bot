module Bot.Domain

open System.Threading.Tasks
open Telegram.Bot.Types
open otsom.fs.Telegram.Bot.Core

type User = { Id: UserId; Lang: string }

type UserConversion =
  { ReceivedMessageId: int
    SentMessageId: BotMessageId
    ConversionId: string
    UserId: UserId }

[<RequireQualifiedAccess>]
module Conversion =
  type New = { Id: string }

  type Prepared = { Id: string; InputFile: string }

  type Converted = { Id: string; OutputFile: string }

  type Thumbnailed = { Id: string; ThumbnailName: string }

  type PreparedOrConverted = Choice<Prepared, Converted>

  type PreparedOrThumbnailed = Choice<Prepared, Thumbnailed>

  type Completed =
    { Id: string
      OutputFile: string
      ThumbnailFile: string }

type Command =
  | Start
  | Links of string seq
  | Document of string * string

type ParseCommand = Message -> Task<Command option>