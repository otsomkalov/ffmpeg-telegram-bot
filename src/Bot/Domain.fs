module Bot.Domain

open System.Threading.Tasks
open Telegram.Bot.Types

type User = { Id: int64; Lang: string }

type UserConversion =
  { ReceivedMessageId: int
    SentMessageId: int
    ConversionId: string
    UserId: int64 }

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