namespace Bot

open System
open System.IO
open System.Net
open System.Net.Http
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open System.Text.RegularExpressions
open System.Threading.Tasks
open Azure.Storage.Blobs
open Azure.Storage.Queues
open FSharp
open Microsoft.AspNetCore.Http
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Azure.Functions.Worker
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.ApplicationInsights
open MongoDB.ApplicationInsights
open MongoDB.Driver
open Polly.Extensions.Http
open Telegram.Bot
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums
open shortid
open MongoDB.ApplicationInsights.DependencyInjection
open Polly
open otsom.FSharp.Extensions
open otsom.FSharp.Extensions.ServiceCollection

module Helpers =
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
module Settings =
  [<CLIMutable>]
  type StorageSettings = { Queue: string }

  [<CLIMutable>]
  type ConverterSettings' = { Queue: string; Container: string }

  [<CLIMutable>]
  type ConverterSettings =
    { Input: ConverterSettings'
      Output: ConverterSettings' }

  [<CLIMutable>]
  type WorkersSettings =
    { ConnectionString: string
      Downloader: StorageSettings
      Converter: ConverterSettings
      Thumbnailer: ConverterSettings
      Uploader: StorageSettings }

    static member SectionName = "Workers"

  [<CLIMutable>]
  type DatabaseSettings =
    { ConnectionString: string
      Name: string }

    static member SectionName = "Database"

  [<CLIMutable>]
  type TelegramSettings =
    { Token: string
      ApiUrl: string }

    static member SectionName = "Telegram"

[<RequireQualifiedAccess>]
module JSON =
  let options =
    JsonFSharpOptions.Default().WithUnionUntagged().WithUnionUnwrapRecordCases()

  let private options' = options.ToJsonSerializerOptions()

  let serialize value =
    JsonSerializer.Serialize(value, options')

[<RequireQualifiedAccess>]
module Telegram =
  let escapeMarkdownString (str: string) =
    Regex.Replace(str, "([`\.#\-!])", "\$1")

  type SendMessage = string -> Task<unit>

  let sendMessage (bot: ITelegramBotClient) (userId: int64) : SendMessage =
    fun text ->
      bot.SendTextMessageAsync((userId |> ChatId), text |> escapeMarkdownString, parseMode = ParseMode.MarkdownV2)
      |> Task.map ignore

  type EditMessage = string -> Task<unit>

  let editMessage (bot: ITelegramBotClient) (userId: int64) messageId : EditMessage =
    fun text ->
      bot.EditMessageTextAsync((userId |> ChatId), messageId, text |> escapeMarkdownString, ParseMode.MarkdownV2)
      |> Task.map ignore

  type ReplyToMessage = string -> Task<int>

  let replyToMessage (bot: ITelegramBotClient) (userId: int64) messageId : ReplyToMessage =
    fun text ->
      bot.SendTextMessageAsync(
        (userId |> ChatId),
        text |> escapeMarkdownString,
        parseMode = ParseMode.MarkdownV2,
        replyToMessageId = messageId
      )
      |> Task.map (fun m -> m.MessageId)

  type DeleteMessage = unit -> Task<unit>

  let deleteMessage (bot: ITelegramBotClient) (userId: int64) messageId : DeleteMessage =
    fun () -> task { do! bot.DeleteMessageAsync((userId |> ChatId), messageId) }

  type ReplyWithVideo = string -> string -> Task<unit>

  let replyWithVideo
    (workersSettings: Settings.WorkersSettings)
    (bot: ITelegramBotClient)
    (userId: int64)
    (messageId: int)
    : ReplyWithVideo =
    fun video thumbnail ->
      let blobServiceClient = BlobServiceClient(workersSettings.ConnectionString)

      let videoContainer =
        blobServiceClient.GetBlobContainerClient workersSettings.Converter.Output.Container

      let thumbnailContainer =
        blobServiceClient.GetBlobContainerClient workersSettings.Thumbnailer.Output.Container

      let videoBlob = videoContainer.GetBlobClient(video)
      let thumbnailBlob = thumbnailContainer.GetBlobClient(thumbnail)

      task {
        let! videoStreamResponse = videoBlob.DownloadStreamingAsync()
        let! thumbnailStreamResponse = thumbnailBlob.DownloadStreamingAsync()

        do!
          bot.SendVideoAsync(
            (userId |> ChatId),
            InputFileStream(videoStreamResponse.Value.Content, video),
            caption = "🇺🇦 Help the Ukrainian army fight russian and belarus invaders: https://savelife.in.ua/en/donate/",
            replyToMessageId = messageId,
            thumbnail = InputFileStream(thumbnailStreamResponse.Value.Content, thumbnail),
            disableNotification = true
          )
          |> Task.map ignore

        do! videoBlob.DeleteAsync() |> Task.map ignore
        do! thumbnailBlob.DeleteAsync() |> Task.map ignore
      }

  type BlobType =
    | Converter
    | Thumbnailer

  let getBlobClient (workersSettings: Settings.WorkersSettings) =
    fun name type' ->
      let blobServiceClient = BlobServiceClient(workersSettings.ConnectionString)

      let container =
        match type' with
        | Converter -> workersSettings.Converter.Input.Container
        | Thumbnailer -> workersSettings.Thumbnailer.Input.Container

      let containerClient = blobServiceClient.GetBlobContainerClient(container)

      containerClient.GetBlobClient(name)

  let getBlobStream (workersSettings: Settings.WorkersSettings) =
    fun name type' ->
      let blobClient = getBlobClient workersSettings name type'

      blobClient.OpenWriteAsync(true)

  type DownloadDocument = string -> string -> Task<string>

  let downloadDocument (bot: ITelegramBotClient) (workersSettings: Settings.WorkersSettings) : DownloadDocument =
    fun id name ->
      task {
        use! converterBlobStream = getBlobStream workersSettings name Converter

        do! bot.GetInfoAndDownloadFileAsync(id, converterBlobStream) |> Task.map ignore

        use! thumbnailerBlobStream = getBlobStream workersSettings name Thumbnailer

        do! bot.GetInfoAndDownloadFileAsync(id, thumbnailerBlobStream) |> Task.map ignore

        return name
      }

[<RequireQualifiedAccess>]
module Queue =
  type File =
    | Link of url: string
    | Document of id: string * name: string

  [<CLIMutable>]
  type DownloaderMessage = { ConversionId: string; File: File }

  let sendDownloaderMessage (workersSettings: Settings.WorkersSettings) =
    fun (message: DownloaderMessage) ->
      let queueServiceClient = QueueServiceClient(workersSettings.ConnectionString)

      let queueClient =
        queueServiceClient.GetQueueClient(workersSettings.Downloader.Queue)

      let messageBody = JSON.serialize message

      queueClient.SendMessageAsync(messageBody) |> Task.map ignore

  [<CLIMutable>]
  type ConverterMessage = { Id: string; Name: string }

  let sendConverterMessage (workersSettings: Settings.WorkersSettings) =
    fun (message: ConverterMessage) ->
      let queueServiceClient = QueueServiceClient(workersSettings.ConnectionString)

      let queueClient =
        queueServiceClient.GetQueueClient(workersSettings.Converter.Input.Queue)

      let messageBody = JSON.serialize message

      queueClient.SendMessageAsync(messageBody) |> Task.map ignore

  type ConversionResult =
    | Success of name: string
    | Error of error: string

  type ConverterResultMessage =
    { Id: string; Result: ConversionResult }

  let sendTumbnailerMessage (workersSettings: Settings.WorkersSettings) =
    fun (message: ConverterMessage) ->
      let queueServiceClient = QueueServiceClient(workersSettings.ConnectionString)

      let queueClient =
        queueServiceClient.GetQueueClient(workersSettings.Thumbnailer.Input.Queue)

      let messageBody = JSON.serialize message

      queueClient.SendMessageAsync(messageBody) |> Task.map ignore

  [<CLIMutable>]
  type UploaderMessage = { ConversionId: string }

  let sendUploaderMessage (workersSettings: Settings.WorkersSettings) =
    fun (message: UploaderMessage) ->
      let queueServiceClient = QueueServiceClient(workersSettings.ConnectionString)

      let queueClient = queueServiceClient.GetQueueClient(workersSettings.Uploader.Queue)

      let messageBody = JSON.serialize message

      queueClient.SendMessageAsync(messageBody) |> Task.map ignore

[<RequireQualifiedAccess>]
module Storage =
  let deleteVideo (workersSettings: Settings.WorkersSettings) =
    fun name ->
      let blobService = BlobServiceClient(workersSettings.ConnectionString)

      let convertedFilesContainer =
        blobService.GetBlobContainerClient(workersSettings.Converter.Output.Container)

      let convertedFileBlob = convertedFilesContainer.GetBlobClient(name)
      convertedFileBlob.DeleteIfExistsAsync() |> Task.map ignore

  let deleteThumbnail (workersSettings: Settings.WorkersSettings) =
    fun name ->
      let blobService = BlobServiceClient(workersSettings.ConnectionString)

      let convertedFilesContainer =
        blobService.GetBlobContainerClient(workersSettings.Thumbnailer.Output.Container)

      let convertedFileBlob = convertedFilesContainer.GetBlobClient(name)
      convertedFileBlob.DeleteIfExistsAsync() |> Task.map ignore

[<RequireQualifiedAccess>]
module Domain =
  type NewConversion =
    { Id: string
      ReceivedMessageId: int
      SentMessageId: int
      UserId: int64 }

  type ConversionState =
    | Prepared of inputFileName: string
    | Converted of outputFileName: string
    | Thumbnailed of thumbnailFileName: string
    | Completed of outputFileName: string * thumbnailFileName: string

  type Conversion =
    { Id: string
      ReceivedMessageId: int
      SentMessageId: int
      UserId: int64
      State: ConversionState }

[<RequireQualifiedAccess>]
module Mappings =
  [<RequireQualifiedAccess>]
  module NewConversion =
    let fromDb (conversion: Database.Conversion) : Domain.NewConversion =
      match conversion.State with
      | Database.ConversionState.New ->
        { Id = conversion.Id
          UserId = conversion.UserId
          ReceivedMessageId = conversion.ReceivedMessageId
          SentMessageId = conversion.SentMessageId }

    let toDb (conversion: Domain.NewConversion) : Database.Conversion =
      Database.Conversion(
        Id = conversion.Id,
        UserId = conversion.UserId,
        ReceivedMessageId = conversion.ReceivedMessageId,
        SentMessageId = conversion.SentMessageId,
        State = Database.ConversionState.New
      )

  [<RequireQualifiedAccess>]
  module Conversion =
    let fromDb (conversion: Database.Conversion) : Domain.Conversion =
      { Id = conversion.Id
        UserId = conversion.UserId
        ReceivedMessageId = conversion.ReceivedMessageId
        SentMessageId = conversion.SentMessageId
        State =
          match conversion.State with
          | Database.ConversionState.Prepared -> Domain.ConversionState.Prepared conversion.InputFileName
          | Database.ConversionState.Converted -> Domain.ConversionState.Converted conversion.OutputFileName
          | Database.ConversionState.Thumbnailed -> Domain.ConversionState.Thumbnailed conversion.ThumbnailFileName
          | Database.ConversionState.Completed -> Domain.ConversionState.Completed(conversion.OutputFileName, conversion.ThumbnailFileName) }

    let toDb (conversion: Domain.Conversion) : Database.Conversion =
      let entity =
        Database.Conversion(
          Id = conversion.Id,
          UserId = conversion.UserId,
          ReceivedMessageId = conversion.ReceivedMessageId,
          SentMessageId = conversion.SentMessageId
        )

      do
        match conversion.State with
        | Domain.Prepared inputFileName ->
          entity.InputFileName <- inputFileName
          entity.State <- Database.ConversionState.Prepared
        | Domain.Converted outputFileName ->
          entity.OutputFileName <- outputFileName
          entity.State <- Database.ConversionState.Converted
        | Domain.Thumbnailed thumbnailFileName ->
          entity.ThumbnailFileName <- thumbnailFileName
          entity.State <- Database.ConversionState.Thumbnailed
        | Domain.Completed(outputFileName, thumbnailFileName) ->
          entity.OutputFileName <- outputFileName
          entity.ThumbnailFileName <- thumbnailFileName
          entity.State <- Database.ConversionState.Completed

      entity

[<RequireQualifiedAccess>]
module Database =

  let loadNewConversion (db: IMongoDatabase) : string -> Task<Domain.NewConversion> =
    let collection = db.GetCollection "conversions"

    fun conversionId ->
      let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

      collection.Find(filter).SingleOrDefaultAsync()
      |> Task.map Mappings.NewConversion.fromDb

  let saveNewConversion (db: IMongoDatabase) =
    let collection = db.GetCollection "conversions"

    fun conversion ->
      let entity = conversion |> Mappings.NewConversion.toDb
      task { do! collection.InsertOneAsync(entity) }

  let saveConversion (db: IMongoDatabase) : Domain.Conversion -> Task<unit> =
    let collection = db.GetCollection "conversions"

    fun conversion ->
      let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversion.Id)

      let entity = conversion |> Mappings.Conversion.toDb
      collection.ReplaceOneAsync(filter, entity) |> Task.map ignore

  let loadConversion (db: IMongoDatabase) : string -> Task<Domain.Conversion> =
    let collection = db.GetCollection "conversions"

    fun conversionId ->
      let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

      collection.Find(filter).SingleOrDefaultAsync()
      |> Task.map Mappings.Conversion.fromDb

[<RequireQualifiedAccess>]
module HTTP =
  type DownloadLinkError =
    | Unauthorized
    | NotFound
    | ServerError

  type DownloadLink = string -> Task<Result<string, DownloadLinkError>>

  let downloadLink (httpClientFactory: IHttpClientFactory) (workersSettings: Settings.WorkersSettings) : DownloadLink =
    let getBlobStream = Telegram.getBlobStream workersSettings

    fun link ->
      task {
        use client = httpClientFactory.CreateClient()
        use request = new HttpRequestMessage(HttpMethod.Get, link)
        use! response = client.SendAsync(request)

        return!
          match response.StatusCode with
          | HttpStatusCode.Unauthorized -> Unauthorized |> Error |> Task.FromResult
          | HttpStatusCode.NotFound -> NotFound |> Error |> Task.FromResult
          | HttpStatusCode.InternalServerError -> ServerError |> Error |> Task.FromResult
          | _ ->
            task {
              let fileName = Path.GetFileName(link)

              use! converterBlobStream = getBlobStream fileName Telegram.Converter
              use! thumbnailerBlobStream = getBlobStream fileName Telegram.Thumbnailer

              do! response.Content.CopyToAsync(converterBlobStream)
              do! response.Content.CopyToAsync(thumbnailerBlobStream)

              return Ok(fileName)
            }
      }

open Helpers

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

#nowarn "20"

module Startup =
  let configureWebApp (builder: IFunctionsWorkerApplicationBuilder) =
    builder.Services.Configure<JsonSerializerOptions>(fun opts -> JSON.options.AddToJsonSerializerOptions(opts))

    ()

  let configureAppConfiguration _ (configBuilder: IConfigurationBuilder) =

    configBuilder.AddUserSecrets(Assembly.GetExecutingAssembly(), true)

    ()

  let configureLogging (builder: ILoggingBuilder) =
    builder.AddFilter<ApplicationInsightsLoggerProvider>(String.Empty, LogLevel.Information)

    ()

  [<Literal>]
  let chromeUserAgent =
    "Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.102 Safari/537.36"

  let private retryPolicy =
    HttpPolicyExtensions
      .HandleTransientHttpError()
      .WaitAndRetryAsync(5, (fun retryAttempt -> TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))))

  let private configureServices (context: HostBuilderContext) (services: IServiceCollection) =
    services.AddApplicationInsightsTelemetryWorkerService()
    services.ConfigureFunctionsApplicationInsights()

    services
      .AddSingletonFunc<Settings.WorkersSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(Settings.WorkersSettings.SectionName)
          .Get<Settings.WorkersSettings>())
      .AddSingletonFunc<Settings.TelegramSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(Settings.TelegramSettings.SectionName)
          .Get<Settings.TelegramSettings>())
      .AddSingletonFunc<Settings.DatabaseSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(Settings.DatabaseSettings.SectionName)
          .Get<Settings.DatabaseSettings>())

    services.AddMongoClientFactory()

    services
      .AddSingletonFunc<IMongoClient, IMongoClientFactory, Settings.DatabaseSettings>(fun factory settings ->
        factory.GetClient settings.ConnectionString)
      .AddSingletonFunc<IMongoDatabase, IMongoClient, Settings.DatabaseSettings>(fun client settings -> client.GetDatabase settings.Name)
      .AddSingletonFunc<ITelegramBotClient, Settings.TelegramSettings>(fun settings ->
        TelegramBotClientOptions(settings.Token, settings.ApiUrl) |> TelegramBotClient :> ITelegramBotClient)

    services
      .AddHttpClient(fun (client: HttpClient) -> client.DefaultRequestHeaders.UserAgent.ParseAdd(chromeUserAgent))
      .AddPolicyHandler(retryPolicy)

    services.AddMvcCore().AddNewtonsoftJson()

    ()

  let host =
    HostBuilder()
      .ConfigureFunctionsWebApplication(configureWebApp)
      .ConfigureAppConfiguration(configureAppConfiguration)
      .ConfigureLogging(configureLogging)
      .ConfigureServices(configureServices)
      .Build()

  // If using the Cosmos, Blob or Tables extension, you will need configure the extensions manually using the extension methods below.
  // Learn more about this here: https://go.microsoft.com/fwlink/?linkid=2245587
  // ConfigureFunctionsWorkerDefaults(fun (context: HostBuilderContext) (appBuilder: IFunctionsWorkerApplicationBuilder) ->
  //     appBuilder.ConfigureCosmosDBExtension() |> ignore
  //     appBuilder.ConfigureBlobStorageExtension() |> ignore
  //     appBuilder.ConfigureTablesExtension() |> ignore
  // ) |> ignore

  host.Run()
