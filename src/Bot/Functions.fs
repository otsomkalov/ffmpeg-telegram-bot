namespace Bot.Functions

open System.Net.Http
open System.Text.RegularExpressions
open System.Threading.Tasks
open Bot
open FSharp
open Microsoft.AspNetCore.Http
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging
open MongoDB.Driver
open Telegram.Bot
open Telegram.Bot.Types
open Helpers
open Telegram.Bot.Types.Enums
open shortid
open otsom.FSharp.Extensions

type Functions
  (
    workersSettings: Settings.WorkersSettings,
    _bot: ITelegramBotClient,
    _db: IMongoDatabase,
    _httpClientFactory: IHttpClientFactory,
    _logger: ILogger<Functions>
  ) =

  let sendDownloaderMessage = Queue.sendDownloaderMessage workersSettings
  let webmLinkRegex = Regex("https?[^ ]*.webm")

  let processMessage (message: Message) =

    let userId = message.Chat.Id
    let sendMessage = Telegram.sendMessage _bot userId
    let replyToMessage = Telegram.replyToMessage _bot userId message.MessageId
    let createConversion = Database.saveNewConversion _db

    match message with
    | FromBot -> Task.FromResult()
    | Text messageText ->
      match messageText with
      | StartsWith "/start" ->
        sendMessage
          "Send me a video or link to WebM or add bot to group. 🇺🇦 Help the Ukrainian army fight russian and belarus invaders: https://savelife.in.ua/en/donate/"
      | Regex webmLinkRegex matches ->

        let sendUrlToQueue (url: string) =
          task {
            let! sentMessageId = replyToMessage $"File {url} is waiting to be downloaded 🕒"

            let newConversion: Domain.NewConversion =
              { Id = ShortId.Generate()
                UserId = userId
                ReceivedMessageId = message.MessageId
                SentMessageId = sentMessageId }

            do! createConversion newConversion

            let message: Queue.DownloaderMessage =
              { ConversionId = newConversion.Id
                File = Queue.File.Link url }

            return! sendDownloaderMessage message
          }

        matches |> Seq.map sendUrlToQueue |> Task.WhenAll |> Task.map ignore
      | _ -> Task.FromResult()
    | Document doc ->
      let sendDocToQueue (doc: Document) =
        task {
          let! sentMessageId = replyToMessage "File is waiting to be downloaded 🕒"

          let newConversion: Domain.NewConversion =
            { Id = ShortId.Generate()
              UserId = userId
              ReceivedMessageId = message.MessageId
              SentMessageId = sentMessageId }

          do! createConversion newConversion

          let message: Queue.DownloaderMessage =
            { ConversionId = newConversion.Id
              File = Queue.File.Document(doc.FileId, doc.FileName) }

          return! sendDownloaderMessage message
        }

      doc |> sendDocToQueue
    | _ -> Task.FromResult()

  let handleUpdate (update: Update) =
    match update.Type with
    | UpdateType.Message -> processMessage update.Message
    | UpdateType.ChannelPost -> processMessage update.ChannelPost
    | _ -> Task.FromResult()

  [<Function("HandleUpdate")>]
  member this.HandleUpdate([<HttpTrigger("POST", Route = "telegram")>] request: HttpRequest, [<FromBody>] update: Update) : Task<unit> =
    task {
      try
        do! handleUpdate update

        return ()
      with e ->
        Logf.elogfe _logger e "Error during processing an update:"
        return ()
    }

  [<Function("Downloader")>]
  member this.DownloadFile
    (
      [<QueueTrigger("%Workers:Downloader:Queue%", Connection = "Workers:ConnectionString")>] message: Queue.DownloaderMessage,
      _: FunctionContext
    ) : Task<unit> =
    let sendConverterMessage = Queue.sendConverterMessage workersSettings
    let sendThumbnailerMessage = Queue.sendTumbnailerMessage workersSettings
    let loadNewConversion = Database.loadNewConversion _db
    let downloadLink = HTTP.downloadLink _httpClientFactory workersSettings
    let downloadFile = Telegram.downloadDocument _bot workersSettings
    let saveConversion = Database.saveConversion _db

    let downloadFile file =
      match file with
      | Queue.File.Document(id, name) -> downloadFile id name |> Task.map Ok
      | Queue.File.Link link -> downloadLink link

    task {
      let! conversion = loadNewConversion message.ConversionId

      let editMessage =
        Telegram.editMessage _bot conversion.UserId conversion.SentMessageId

      return!
        message.File
        |> downloadFile
        |> Task.bind (function
          | Ok file ->
            task {
              let preparedConversion: Domain.Conversion =
                { Id = message.ConversionId
                  UserId = conversion.UserId
                  ReceivedMessageId = conversion.ReceivedMessageId
                  SentMessageId = conversion.SentMessageId
                  State = Domain.ConversionState.Prepared file }

              do! saveConversion preparedConversion

              let converterMessage: Queue.ConverterMessage = { Id = conversion.Id; Name = file }

              do! sendConverterMessage converterMessage

              let thumbnailerMessage: Queue.ConverterMessage = { Id = conversion.Id; Name = file }

              do! sendThumbnailerMessage thumbnailerMessage

              do! editMessage "Conversion is in progress 🚀"
            }
          | Error(HTTP.DownloadLinkError.Unauthorized) -> editMessage "I am not authorized to download video from this source 🚫"
          | Error(HTTP.DownloadLinkError.NotFound) -> editMessage "Video not found ⚠️"
          | Error(HTTP.DownloadLinkError.ServerError) -> editMessage "Server error 🛑")
    }

  [<Function("SaveConversionResult")>]
  member this.SaveConversionResult
    (
      [<QueueTrigger("%Workers:Converter:Output:Queue%", Connection = "Workers:ConnectionString")>] message: Queue.ConverterResultMessage,
      _: FunctionContext
    ) : Task<unit> =
    let loadConversion = Database.loadConversion _db
    let saveConversion = Database.saveConversion _db
    let sendUploaderMessage = Queue.sendUploaderMessage workersSettings

    task {
      let! conversion = loadConversion message.Id

      let editMessage =
        Telegram.editMessage _bot conversion.UserId conversion.SentMessageId

      return!
        match message.Result with
        | Queue.Success file ->
          match conversion.State with
          | Domain.Prepared inputFileName ->
            let convertedConversion: Domain.Conversion =
              { Id = conversion.Id
                UserId = conversion.UserId
                ReceivedMessageId = conversion.ReceivedMessageId
                SentMessageId = conversion.SentMessageId
                State = Domain.ConversionState.Converted file }

            task {
              do! saveConversion convertedConversion
              do! editMessage "Video successfully converted! Generating the thumbnail..."
            }
          | Domain.Thumbnailed thumbnailFileName ->
            let completedConversion: Domain.Conversion =
              { Id = conversion.Id
                UserId = conversion.UserId
                ReceivedMessageId = conversion.ReceivedMessageId
                SentMessageId = conversion.SentMessageId
                State = Domain.ConversionState.Completed(file, thumbnailFileName) }

            let uploaderMessage: Queue.UploaderMessage = { ConversionId = conversion.Id }

            task {
              do! saveConversion completedConversion
              do! sendUploaderMessage uploaderMessage
              do! editMessage "File successfully converted! Uploading the file 🚀"
            }
        | Queue.Error error -> editMessage error
    }

  [<Function("SaveThumbnailingResult")>]
  member this.SaveThumbnailingResult
    (
      [<QueueTrigger("%Workers:Thumbnailer:Output:Queue%", Connection = "Workers:ConnectionString")>] message: Queue.ConverterResultMessage,
      _: FunctionContext
    ) : Task<unit> =
    let loadConversion = Database.loadConversion _db
    let saveConversion = Database.saveConversion _db
    let sendUploaderMessage = Queue.sendUploaderMessage workersSettings

    task {
      let! conversion = loadConversion message.Id

      let editMessage =
        Telegram.editMessage _bot conversion.UserId conversion.SentMessageId

      return!
        match message.Result with
        | Queue.Success file ->
          match conversion.State with
          | Domain.Prepared inputFileName ->
            let convertedConversion: Domain.Conversion =
              { Id = conversion.Id
                UserId = conversion.UserId
                ReceivedMessageId = conversion.ReceivedMessageId
                SentMessageId = conversion.SentMessageId
                State = Domain.ConversionState.Thumbnailed file }

            task {
              do! saveConversion convertedConversion
              do! editMessage "Thumbnail generated! Converting the video..."
            }
          | Domain.Converted convertedFileName ->
            let completedConversion: Domain.Conversion =
              { Id = conversion.Id
                UserId = conversion.UserId
                ReceivedMessageId = conversion.ReceivedMessageId
                SentMessageId = conversion.SentMessageId
                State = Domain.ConversionState.Completed(convertedFileName, file) }

            let uploaderMessage: Queue.UploaderMessage = { ConversionId = conversion.Id }

            task {
              do! saveConversion completedConversion
              do! sendUploaderMessage uploaderMessage
              do! editMessage "File successfully converted! Uploading the file 🚀"
            }
        | Queue.Error error ->

          editMessage error
    }

  [<Function("Uploader")>]
  member this.Upload
    (
      [<QueueTrigger("%Workers:Uploader:Queue%", Connection = "Workers:ConnectionString")>] message: Queue.UploaderMessage,
      _: FunctionContext
    ) : Task =
    let loadConversion = Database.loadConversion _db

    task {
      let! conversion = loadConversion message.ConversionId

      let deleteMessage =
        Telegram.deleteMessage _bot conversion.UserId conversion.SentMessageId

      let replyWithVideo =
        Telegram.replyWithVideo workersSettings _bot conversion.UserId conversion.ReceivedMessageId

      let deleteVideo = Storage.deleteVideo workersSettings
      let deleteThumbnail = Storage.deleteThumbnail workersSettings

      match conversion.State with
      | Domain.Completed(outputFileName, thumbnailFileName) ->
        do! replyWithVideo outputFileName thumbnailFileName
        do! deleteMessage ()

        do! deleteVideo outputFileName
        do! deleteThumbnail thumbnailFileName
    }