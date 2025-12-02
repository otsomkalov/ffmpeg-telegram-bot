module Telegram.Handlers

open System.IO
open System.Text.RegularExpressions
open System.Threading.Tasks
open Domain
open Domain.Core
open Domain.Core.Conversion
open Microsoft.Extensions.Logging
open Telegram.Repos
open Telegram.Settings
open otsom.fs.Bot
open otsom.fs.Resources
open otsom.fs.Extensions
open Telegram.Helpers
open FsToolkit.ErrorHandling

type MsgHandler = UserMsg -> Task<unit option>
type MsgHandlerFactory = IBotService -> IResourceProvider -> MsgHandler

let startHandler (bot: IBotService) (resp: IResourceProvider) : MsgHandler =
  fun msg ->
    task {
      match msg.Text with
      | Some "/start" ->
        do! bot.ReplyToMessage(msg.MessageId, resp[Resources.Welcome]) |> Task.ignore

        return Some()
      | _ -> return None
    }

let private queueProcessing
  (createConversion: Create)
  (userConversionRepo: IUserConversionRepo)
  (conversionRepo: IConversionRepo)
  =
  fun userMessageId chatId sentMessageId inputFile ->
    task {
      let! conversion = createConversion ()

      do!
        userConversionRepo.SaveUserConversion
          { ChatId = chatId
            SentMessageId = sentMessageId
            ReceivedMessageId = userMessageId
            ConversionId = conversion.Id }

      do! conversionRepo.QueuePreparation(conversion.Id, inputFile)
    }

let linksHandler
  (createConversion: Create)
  (userConversionRepo: IUserConversionRepo)
  (conversionRepo: IConversionRepo)
  (settings: InputValidationSettings)
  (logger: ILogger<MsgHandler>)
  (bot: IBotService)
  (resp: IResourceProvider)
  : MsgHandler =
  let queueProcessing =
    queueProcessing createConversion userConversionRepo conversionRepo

  let linkRegex = Regex(settings.LinkRegex)

  fun msg ->
    task {
      match msg.Text with
      | Some(Regex linkRegex links) ->
        logger.LogInformation("Processing message with links")

        for link in links do
          let! sentMessageId = bot.ReplyToMessage(msg.MessageId, resp[Resources.LinkDownload, [| link |]])

          do! queueProcessing msg.MessageId msg.ChatId sentMessageId (Conversion.New.InputFile.Link { Url = link })

        return Some()
      | _ -> return None
    }

let documentHandler
  (createConversion: Create)
  (userConversionRepo: IUserConversionRepo)
  (conversionRepo: IConversionRepo)
  (settings: InputValidationSettings)
  (logger: ILogger<MsgHandler>)
  (bot: IBotService)
  (resp: IResourceProvider)
  : MsgHandler =
  let queueProcessing =
    queueProcessing createConversion userConversionRepo conversionRepo

  fun msg ->
    task {
      match msg.Doc with
      | Some doc when
        settings.MimeTypes |> Seq.contains doc.MimeType
        && doc.Caption |> Option.contains "!nsfw" |> not
        ->
        logger.LogInformation("Processing message with document {DocumentName}", doc.Name)

        let! sentMessageId =
          bot.ReplyToMessage(msg.MessageId, resp[Resources.DocumentDownload, [| cleanFileName doc.Name |]])

        do!
          queueProcessing
            msg.MessageId
            msg.ChatId
            sentMessageId
            (Conversion.New.InputFile.Document { Id = doc.Id; Name = doc.Name })

        return Some()
      | _ -> return None
    }

let videoHandler
  (createConversion: Create)
  (userConversionRepo: IUserConversionRepo)
  (conversionRepo: IConversionRepo)
  (settings: InputValidationSettings)
  (logger: ILogger<MsgHandler>)
  (bot: IBotService)
  (resp: IResourceProvider)
  : MsgHandler =
  let queueProcessing =
    queueProcessing createConversion userConversionRepo conversionRepo

  fun msg ->
    task {
      match msg.Vid with
      | Some vid when
        settings.MimeTypes |> Seq.contains vid.MimeType
        && vid.Caption |> Option.contains "!nsfw" |> not
        ->
        logger.LogInformation("Processing message with video {VideoName}", vid.Name)

        let videoName =
          vid.Name
          |> Option.map cleanFileName
          |> Option.defaultWith (fun _ ->
            let tmpFile = Path.GetTempFileName()
            let fileInfo = FileInfo(tmpFile)

            fileInfo.Name)

        let! sentMessageId = bot.ReplyToMessage(msg.MessageId, resp[Resources.VideoDownload, [| videoName |]])

        do!
          queueProcessing
            msg.MessageId
            msg.ChatId
            sentMessageId
            (Conversion.New.InputFile.Document { Id = vid.Id; Name = videoName })

        return Some()
      | _ -> return None
    }