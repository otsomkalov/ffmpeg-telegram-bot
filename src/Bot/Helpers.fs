module Bot.Helpers

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.RegularExpressions
open Telegram.Bot.Types

[<RequireQualifiedAccess>]
module String =
  let compareCI input toCompare =
    String.Equals(input, toCompare, StringComparison.InvariantCultureIgnoreCase)

  let containsCI (input: string) (toSearch: string) =
    input.Contains(toSearch, StringComparison.InvariantCultureIgnoreCase)

let private contains (substring: string) (str: string) =
  str.Contains(substring, StringComparison.InvariantCultureIgnoreCase)

let (|Text|_|) (message: Message) =
  message
  |> Option.ofObj
  |> Option.bind (fun m -> m.Text |> Option.ofObj)
  |> Option.filter (fun t -> String.containsCI t "!nsfw" |> not)
  |> Option.filter (String.IsNullOrEmpty >> not)

let (|Document|_|) (message: Message) =
  message
  |> Option.ofObj
  |> Option.filter (fun m -> String.IsNullOrEmpty m.Caption || (String.containsCI m.Caption "!nsfw" |> not))
  |> Option.bind (fun m -> m.Document |> Option.ofObj)
  |> Option.filter (fun d ->
    String.compareCI (Path.GetExtension(d.FileName)) ".webm"
    && String.compareCI d.MimeType "video/webm")

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