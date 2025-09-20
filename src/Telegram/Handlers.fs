module Telegram.Handlers

open System.IO
open System.Text.RegularExpressions
open System.Threading.Tasks
open Domain.Core
open Telegram.Settings
open otsom.fs.Bot
open otsom.fs.Resources
open otsom.fs.Extensions
open Telegram.Helpers

type Doc =
  { Id: string
    Name: string
    Caption: string option
    MimeType: string }

type Vid =
  { Id: string
    Name: string option
    Caption: string option
    MimeType: string }

type Msg =
  { MessageId: ChatMessageId
    Text: string option
    Doc: Doc option
    Vid: Vid option }

type MsgHandler = Msg -> Task<unit option>

let startHandler (bot: IBotService) (resp: IResourceProvider) : MsgHandler =
  fun msg ->
    task {
      match msg.Text with
      | Some "/start" ->
        do! bot.ReplyToMessage(msg.MessageId, resp[Resources.Welcome]) |> Task.ignore

        return Some()
      | _ -> return None
    }

let linksHandler queueUserConversion (settings: InputValidationSettings) (bot: IBotService) (resp: IResourceProvider) : MsgHandler =
  let linkRegex = Regex(settings.LinkRegex)

  fun msg ->
    task {
      match msg.Text with
      | Some(Regex linkRegex links) ->
        for link in links do
          let! sentMessageId = bot.ReplyToMessage(msg.MessageId, resp[Resources.LinkDownload, [| link |]])

          do! queueUserConversion sentMessageId (Conversion.New.InputFile.Link { Url = link })

        return Some()
      | _ -> return None
    }

let documentHandler queueUserConversion (settings: InputValidationSettings) (bot: IBotService) (resp: IResourceProvider) : MsgHandler =
  fun msg ->
    task {
      match msg.Doc with
      | Some doc when
        settings.MimeTypes |> Seq.contains doc.MimeType
        && doc.Caption |> Option.contains "!nsfw"
        ->
        let! sentMessageId = bot.ReplyToMessage(msg.MessageId, resp[Resources.DocumentDownload, [| doc.Name |]])

        do! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = doc.Id; Name = doc.Name })

        return Some()
      | _ -> return None
    }

let videoHandler queueUserConversion (settings: InputValidationSettings) (bot: IBotService) (resp: IResourceProvider) : MsgHandler =
  fun msg ->
    task {
      match msg.Vid with
      | Some vid when
        settings.MimeTypes |> Seq.contains vid.MimeType
        && vid.Caption |> Option.contains "!nsfw"
        ->
        let videoName =
          vid.Name
          |> Option.defaultWith (fun _ ->
            let tmpFile = Path.GetTempFileName()

            // TODO: just return tmpFile?
            let fileInfo = FileInfo(tmpFile)

            fileInfo.Name)

        let! sentMessageId = bot.ReplyToMessage(msg.MessageId, resp[Resources.VideoDownload, [| videoName |]])

        do! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = vid.Id; Name = videoName })

        return Some()
      | _ -> return None
    }

type MsgHandlerFactory = IBotService -> IResourceProvider -> MsgHandler

type GlobalHandler = Msg -> Task<unit>

let globalHandler bot resp (handlerFactories: MsgHandlerFactory seq) : GlobalHandler =
  fun msg -> task {
    let handlers = handlerFactories |> Seq.map (fun f -> f bot resp)

    let mutable lastHandlerResult = None
    let mutable e = handlers.GetEnumerator()

    while e.MoveNext() && lastHandlerResult.IsNone do
      let! handlerResult = e.Current msg

      lastHandlerResult <- handlerResult

    return ()
  }