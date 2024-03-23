module Bot.Domain

open System.Threading.Tasks
open Telegram.Bot.Types
open otsom.fs.Telegram.Bot.Core

type User = { Id: UserId; Lang: string option }

[<RequireQualifiedAccess>]
module Conversion =
  type New = { Id: string }

  type Prepared = { Id: string; InputFile: string }

  type Converted = { Id: string; OutputFile: string }

  type Thumbnailed = { Id: string; ThumbnailName: string }

  type PreparedOrConverted = Choice<Prepared, Converted>

  type PreparedOrThumbnailed = Choice<Prepared, Thumbnailed>

[<RequireQualifiedAccess>]
type Command =
  | Start
  | Links of string seq
  | Document of string * string
  | Video of string * string

type ParseCommand = Message -> Task<Command option>