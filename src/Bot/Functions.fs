namespace Bot.Functions

open System.Net.Http
open System.Threading.Tasks
open Bot
open Bot.Domain
open Bot.Database
open Bot.Translation
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
open otsom.fs.Extensions
open otsom.fs.Telegram.Bot.Core

type Functions
  (
    workersSettings: Settings.WorkersSettings,
    _bot: ITelegramBotClient,
    _db: IMongoDatabase,
    _httpClientFactory: IHttpClientFactory,
    _logger: ILogger<Functions>,
    getLocaleTranslations: GetLocaleTranslations,
    sendUserMessage: SendUserMessage,
    replyToUserMessage: ReplyToUserMessage,
    editBotMessage: EditBotMessage,
    defaultLocaleTranslations: DefaultLocaleTranslations
  ) =

  let sendDownloaderMessage = Queue.sendDownloaderMessage workersSettings

  let processMessage (message: Message) =

    let chatId = message.Chat.Id |> UserId
    let userId = message.From |> Option.ofObj |> Option.map (_.Id >> UserId)
    let sendMessage = sendUserMessage chatId
    let replyToMessage = replyToUserMessage chatId message.MessageId
    let saveUserConversion = UserConversion.save _db
    let saveConversion = Conversion.New.save _db
    let tran, tranf =
      match message.From |> Option.ofObj |> Option.map (_.LanguageCode) with
      | Some lang -> getLocaleTranslations lang
      | None -> defaultLocaleTranslations
    let ensureUserExists = User.ensureExists _db

    let processLinks links =
      let sendUrlToQueue (url: string) =
        task {
          let! sentMessageId = replyToMessage (tranf(Resources.LinkDownload, [|url|]))

          let newConversion: Domain.Conversion.New = { Id = ShortId.Generate() }

          do! saveConversion newConversion

          let userConversion: Domain.UserConversion =
            { ConversionId = newConversion.Id
              UserId = userId
              SentMessageId = sentMessageId
              ReceivedMessageId = message.MessageId
              ChatId = chatId }

          do! saveUserConversion userConversion

          let message: Queue.DownloaderMessage =
            { ConversionId = newConversion.Id
              File = Queue.File.Link url }

          return! sendDownloaderMessage message
        }

      links |> Seq.map sendUrlToQueue |> Task.WhenAll |> Task.ignore

    let processDocument fileId fileName =
      task {
        let! sentMessageId = replyToMessage (tranf (Resources.DocumentDownload, [|fileName|]))

        let newConversion: Domain.Conversion.New = { Id = ShortId.Generate() }

        do! saveConversion newConversion

        let userConversion: Domain.UserConversion =
          { ConversionId = newConversion.Id
            UserId = userId
            SentMessageId = sentMessageId
            ReceivedMessageId = message.MessageId
            ChatId = chatId }

        do! saveUserConversion userConversion

        let message: Queue.DownloaderMessage =
          { ConversionId = newConversion.Id
            File = Queue.File.Document(fileId, fileName) }

        return! sendDownloaderMessage message
      }

    let processCommand =
      function
      | Start ->
        sendMessage (tran Resources.Welcome)
      | Links links -> processLinks links
      | Document(fileId, fileName) -> processDocument fileId fileName

    let processMessage' =
      function
      | None -> Task.FromResult()
      | Some cmd ->
        ensureUserExists (Mappings.User.fromTg message.From)
        |> Task.bind(fun () -> processCommand cmd)

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
    let loadUser = User.load _db

    let downloadFile file =
      match file with
      | Queue.File.Document(id, name) -> downloadFile id name |> Task.map Ok
      | Queue.File.Link link -> downloadLink link

    task {
      let! userConversion = loadUserConversion message.ConversionId
      let! tran, _ =
        match userConversion.UserId with
        | Some id -> loadUser id |> Task.map (fun u -> getLocaleTranslations u.Lang)
        | None -> defaultLocaleTranslations |> Task.FromResult

      let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

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

              do! editMessage (tran Resources.ConversionInProgress)
            }
          | Error(HTTP.DownloadLinkError.Unauthorized) -> editMessage Resources.NotAuthorized
          | Error(HTTP.DownloadLinkError.NotFound) -> editMessage Resources.NotFound
          | Error(HTTP.DownloadLinkError.ServerError) -> editMessage Resources.ServerError)
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
    let loadUser = User.load _db

    task {
      let! userConversion = loadUserConversion message.Id

      let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

      let! tran, _ =
        match userConversion.UserId with
        | Some id -> loadUser id |> Task.map (fun u -> getLocaleTranslations u.Lang)
        | None -> defaultLocaleTranslations |> Task.FromResult

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
              do! editMessage (tran Resources.VideoConverted)
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
              do! editMessage (tran Resources.Uploading)
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
    let loadUser = User.load _db

    task {
      let! userConversion = loadUserConversion message.Id

      let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

      let! tran, _ =
        match userConversion.UserId with
        | Some id -> loadUser id |> Task.map (fun u -> getLocaleTranslations u.Lang)
        | None -> defaultLocaleTranslations |> Task.FromResult

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
              do! editMessage (tran Resources.ThumbnailGenerated)
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
              do! editMessage (tran Resources.Uploading)
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
        Telegram.deleteMessage _bot userConversion.ChatId userConversion.SentMessageId

      let replyWithVideo =
        Telegram.replyWithVideo workersSettings _bot userConversion.ChatId userConversion.ReceivedMessageId

      let deleteVideo = Storage.deleteVideo workersSettings
      let deleteThumbnail = Storage.deleteThumbnail workersSettings

      let! conversion = loadCompletedConversion message.ConversionId

      do! replyWithVideo conversion.OutputFile conversion.ThumbnailFile
      do! deleteMessage ()

      do! deleteVideo conversion.OutputFile
      do! deleteThumbnail conversion.ThumbnailFile
    }
