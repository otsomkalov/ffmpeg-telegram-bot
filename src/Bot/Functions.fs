namespace Bot.Functions

open System.Net.Http
open System.Threading.Tasks
open Bot
open Bot.Domain
open Bot.Database
open FSharp
open Microsoft.AspNetCore.Http
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging
open MongoDB.Driver
open Telegram.Bot
open Telegram.Bot.Types
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

  let processMessage (message: Message) =

    let userId = message.Chat.Id
    let sendMessage = Telegram.sendMessage _bot userId
    let replyToMessage = Telegram.replyToMessage _bot userId message.MessageId
    let saveUserConversion = UserConversion.save _db
    let saveConversion = Conversion.New.save _db
    let ensureUserExists = User.ensureExists _db

    let processLinks links =
      let sendUrlToQueue (url: string) =
        task {
          let! sentMessageId = replyToMessage $"File {url} is waiting to be downloaded 🕒"

          let newConversion: Domain.Conversion.New = { Id = ShortId.Generate() }

          do! saveConversion newConversion

          let userConversion: Domain.UserConversion =
            { ConversionId = newConversion.Id
              UserId = userId
              SentMessageId = sentMessageId
              ReceivedMessageId = message.MessageId }

          do! saveUserConversion userConversion

          let message: Queue.DownloaderMessage =
            { ConversionId = newConversion.Id
              File = Queue.File.Link url }

          return! sendDownloaderMessage message
        }

      links |> Seq.map sendUrlToQueue |> Task.WhenAll |> Task.map ignore

    let processDocument fileId fileName =
      task {
        let! sentMessageId = replyToMessage "File is waiting to be downloaded 🕒"

        let newConversion: Domain.Conversion.New = { Id = ShortId.Generate() }

        do! saveConversion newConversion

        let userConversion: Domain.UserConversion =
          { ConversionId = newConversion.Id
            UserId = userId
            SentMessageId = sentMessageId
            ReceivedMessageId = message.MessageId }

        do! saveUserConversion userConversion

        let message: Queue.DownloaderMessage =
          { ConversionId = newConversion.Id
            File = Queue.File.Document(fileId, fileName) }

        return! sendDownloaderMessage message
      }

    let processMessage' =
      function
      | None -> Task.FromResult()
      | Some Start ->
        sendMessage
          "Send me a video or link to WebM or add bot to group. 🇺🇦 Help the Ukrainian army fight russian and belarus invaders: https://savelife.in.ua/en/donate/"
      | Some(Links links) -> processLinks links
      | Some(Document(fileId, fileName)) -> processDocument fileId fileName

    Workflows.parseCommand message |> Task.bind processMessage'

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
    let loadUserConversion = UserConversion.load _db
    let loadNewConversion = Conversion.New.load _db
    let downloadLink = HTTP.downloadLink _httpClientFactory workersSettings
    let downloadFile = Telegram.downloadDocument _bot workersSettings
    let savePreparedConversion = Conversion.Prepared.save _db

    let downloadFile file =
      match file with
      | Queue.File.Document(id, name) -> downloadFile id name |> Task.map Ok
      | Queue.File.Link link -> downloadLink link

    task {
      let! userConversion = loadUserConversion message.ConversionId

      let editMessage =
        Telegram.editMessage _bot userConversion.UserId userConversion.SentMessageId

      let! conversion = loadNewConversion message.ConversionId

      return!
        message.File
        |> downloadFile
        |> Task.bind (function
          | Ok file ->
            task {
              let preparedConversion: Domain.Conversion.Prepared =
                { Id = message.ConversionId
                  InputFile = file }

              do! savePreparedConversion preparedConversion

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
    let loadUserConversion = UserConversion.load _db
    let loadPreparedOrThumbnailed = Conversion.PreparedOrThumbnailed.load _db
    let saveConvertedConversion = Conversion.Converted.save _db
    let saveCompletedConversion = Conversion.Completed.save _db
    let sendUploaderMessage = Queue.sendUploaderMessage workersSettings

    task {
      let! userConversion = loadUserConversion message.Id

      let editMessage =
        Telegram.editMessage _bot userConversion.UserId userConversion.SentMessageId

      let! conversion = loadPreparedOrThumbnailed message.Id

      return!
        match message.Result with
        | Queue.Success file ->
          match conversion with
          | Choice1Of2 preparedConversion ->
            let convertedConversion: Domain.Conversion.Converted =
              { Id = preparedConversion.Id
                OutputFile = file }

            task {
              do! saveConvertedConversion convertedConversion
              do! editMessage "Video successfully converted! Generating the thumbnail..."
            }
          | Choice2Of2 thumbnailedConversion ->
            let completedConversion: Domain.Conversion.Completed =
              { Id = thumbnailedConversion.Id
                OutputFile = file
                ThumbnailFile = thumbnailedConversion.ThumbnailName }

            let uploaderMessage: Queue.UploaderMessage =
              { ConversionId = thumbnailedConversion.Id }

            task {
              do! saveCompletedConversion completedConversion
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
    let loadUserConversion = UserConversion.load _db
    let loadPreparedOrConverted = Conversion.PreparedOrConverted.load _db
    let saveThumbnailedConversion = Conversion.Thumbnailed.save _db
    let saveCompletedConversion = Conversion.Completed.save _db
    let sendUploaderMessage = Queue.sendUploaderMessage workersSettings

    task {
      let! userConversion = loadUserConversion message.Id

      let editMessage =
        Telegram.editMessage _bot userConversion.UserId userConversion.SentMessageId

      let! conversion = loadPreparedOrConverted message.Id

      return!
        match message.Result with
        | Queue.Success file ->
          match conversion with
          | Choice1Of2 preparedConversion ->
            let thumbnailedConversion: Domain.Conversion.Thumbnailed =
              { Id = preparedConversion.Id
                ThumbnailName = file }

            task {
              do! saveThumbnailedConversion thumbnailedConversion
              do! editMessage "Thumbnail generated! Converting the video..."
            }
          | Choice2Of2 convertedConversion ->
            let completedConversion: Domain.Conversion.Completed =
              { Id = convertedConversion.Id
                OutputFile = convertedConversion.OutputFile
                ThumbnailFile = file }

            let uploaderMessage: Queue.UploaderMessage =
              { ConversionId = convertedConversion.Id }

            task {
              do! saveCompletedConversion completedConversion
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
    let loadUserConversion = UserConversion.load _db
    let loadCompletedConversion = Conversion.Completed.load _db

    task {
      let! userConversion = loadUserConversion message.ConversionId

      let deleteMessage =
        Telegram.deleteMessage _bot userConversion.UserId userConversion.SentMessageId

      let replyWithVideo =
        Telegram.replyWithVideo workersSettings _bot userConversion.UserId userConversion.ReceivedMessageId

      let deleteVideo = Storage.deleteVideo workersSettings
      let deleteThumbnail = Storage.deleteThumbnail workersSettings

      let! conversion = loadCompletedConversion message.ConversionId

      do! replyWithVideo conversion.OutputFile conversion.ThumbnailFile
      do! deleteMessage ()

      do! deleteVideo conversion.OutputFile
      do! deleteThumbnail conversion.ThumbnailFile
    }
