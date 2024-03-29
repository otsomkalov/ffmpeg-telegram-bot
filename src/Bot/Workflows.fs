module Bot.Workflows

open System.Text.RegularExpressions
open Bot.Domain
open System.Threading.Tasks
open Domain.Core
open FSharp
open Helpers
open Microsoft.Extensions.Logging
open Telegram.Core
open otsom.fs.Telegram.Bot.Core

[<RequireQualifiedAccess>]
module User =
  type Load = UserId -> Task<User>
  type Save = User -> Task<unit>
  type EnsureExists = User -> Task<unit>

[<RequireQualifiedAccess>]
module UserConversion =
  type Save = UserConversion -> unit Task

[<RequireQualifiedAccess>]
module Conversion =
  [<RequireQualifiedAccess>]
  module New =
    type Load = string -> Conversion.New Task
    type Save = Conversion.New -> unit Task

  [<RequireQualifiedAccess>]
  module Prepared =
    type Load = string -> Conversion.Prepared Task
    type Save = Conversion.Prepared -> unit Task

  [<RequireQualifiedAccess>]
  module Converted =
    type Save = Conversion.Converted -> unit Task

  [<RequireQualifiedAccess>]
  module Thumbnailed =
    type Save = Conversion.Thumbnailed -> unit Task

  [<RequireQualifiedAccess>]
  module PreparedOrConverted =
    type Load = string -> Conversion.PreparedOrConverted Task

  [<RequireQualifiedAccess>]
  module PreparedOrThumbnailed =
    type Load = string -> Conversion.PreparedOrThumbnailed Task

  [<RequireQualifiedAccess>]
  module Completed =
    type Save = Conversion.Completed -> unit Task

let parseCommand (settings: Settings.InputValidationSettings) (loggerFactory: ILoggerFactory) : ParseCommand =
  let logger = loggerFactory.CreateLogger(nameof(ParseCommand))
  let linkRegex = Regex(settings.LinkRegex)

  fun message ->
    Logf.logfi logger "Parsing input command from message"

    match message with
    | FromBot ->
      None |> Task.FromResult
    | Text messageText ->
      match messageText with
      | StartsWith "/start" ->
        Command.Start |> Some |> Task.FromResult
      | Regex linkRegex matches ->
        matches |> Command.Links |> Some |> Task.FromResult
      | _ ->
        None |> Task.FromResult
    | Document settings.MimeTypes doc ->
      Command.Document(doc.FileId, doc.FileName) |> Some |> Task.FromResult
    | Video settings.MimeTypes vid ->
      Command.Video(vid.FileId, vid.FileName) |> Some |> Task.FromResult
    | _ ->
      None |> Task.FromResult
