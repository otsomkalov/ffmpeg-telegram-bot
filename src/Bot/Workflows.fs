module Bot.Workflows

open System.Text.RegularExpressions
open Bot.Domain
open System.Threading.Tasks
open Helpers

[<RequireQualifiedAccess>]
module User =
  type Load = int64 -> Task<User>
  type Save = User -> Task<unit>
  type EnsureExists = User -> Task<unit>

[<RequireQualifiedAccess>]
module User =
  type Load = int64 -> Task<User>
  type Save = User -> Task<unit>
  type EnsureExists = User -> Task<unit>

[<RequireQualifiedAccess>]
module UserConversion =
  type Load = string -> UserConversion Task
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
    type Load = string -> Conversion.Completed Task
    type Save = Conversion.Completed -> unit Task

let parseCommand : ParseCommand =
  let webmLinkRegex = Regex("https?[^ ]*.webm")

  function
  | FromBot ->
    None |> Task.FromResult
  | Text messageText ->
    match messageText with
    | StartsWith "/start" ->
      Command.Start |> Some |> Task.FromResult
    | Regex webmLinkRegex matches ->
      matches |> Command.Links |> Some |> Task.FromResult
    | _ ->
      None |> Task.FromResult
  | Document doc ->
    Command.Document(doc.FileId, doc.FileName) |> Some |> Task.FromResult
  | _ ->
    None |> Task.FromResult
