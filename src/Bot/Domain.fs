module Bot.Domain

open System.Threading.Tasks
open Domain.Core
open Telegram.Bot.Types

[<RequireQualifiedAccess>]
module Conversion =
  type New = { Id: string }

  type PreparedOrThumbnailed = Choice<Conversion.Prepared, Conversion.Thumbnailed>

type Command =
  | Start
  | Links of string seq
  | Document of string * string
  | Video of string * string

type ParseCommand = Message -> Task<Command option>