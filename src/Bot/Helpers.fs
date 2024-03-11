module Bot.Helpers

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.RegularExpressions
open Telegram.Bot.Types
open otsom.fs.Extensions.String

let (|Text|_|) (message: Message) =
  message
  |> Option.ofObj
  |> Option.bind (fun m -> m.Text |> Option.ofObj)
  |> Option.filter (function | Contains "!nsfw" -> false | _ -> true)
  |> Option.filter (String.IsNullOrEmpty >> not)

let (|Document|_|) (mimeTypes: string seq) (message: Message) =
  message
  |> Option.ofObj
  |> Option.filter (fun m -> String.IsNullOrEmpty m.Caption || (match m.Caption with | Contains "!nsfw" -> false | _ -> true))
  |> Option.bind (fun m -> m.Document |> Option.ofObj)
  |> Option.filter(fun m -> mimeTypes |> Seq.contains m.MimeType )

let (|Video|_|) (message: Message) =
  message
  |> Option.ofObj
  |> Option.filter (fun m -> String.IsNullOrEmpty m.Caption || (match m.Caption with | Contains "!nsfw" -> false | _ -> true))
  |> Option.bind (fun m -> m.Video |> Option.ofObj)
  |> Option.bind (fun d ->
    match (Path.GetExtension(d.FileName), d.MimeType) with
    | Equals ".webm", Equals "video/webm" -> Some d
    | _ -> None)

let (|FromBot|_|) (message: Message) =
  message.From
  |> Option.ofObj
  |> Option.filter (fun u -> u.IsBot)
  |> Option.map ignore

let (|StartsWith|_|) (substring: string) (str: string) =
  if str.StartsWith(substring, StringComparison.InvariantCultureIgnoreCase) then
    Some()
  else
    None

let (|Regex|_|) (regex: Regex) (text: string) =
  let matches = regex.Matches text

  if matches |> Seq.isEmpty then
    None
  else
    matches |> Seq.map (fun m -> m.Value) |> Some

[<RequireQualifiedAccess>]
module JSON =
  let options =
    JsonFSharpOptions.Default().WithUnionUntagged().WithUnionUnwrapRecordCases()

  let private options' = options.ToJsonSerializerOptions()

  let serialize value =
    JsonSerializer.Serialize(value, options')