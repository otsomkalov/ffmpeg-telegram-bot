module Bot.Domain

open System.Threading.Tasks
open Domain.Core
open Telegram.Bot.Types

[<RequireQualifiedAccess>]
type Command =
  | Start
  | Links of string seq
  | Document of string * string
  | Video of string * string

type ParseCommand = Message -> Task<Command option>