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
open Microsoft.AspNetCore.Http
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Azure.Functions.Worker
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.ApplicationInsights
open Microsoft.Extensions.Options
open MongoDB.ApplicationInsights
open MongoDB.Driver
open Polly.Extensions.Http
open Telegram.Bot
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums
open shortid
open MongoDB.ApplicationInsights.DependencyInjection
open Polly

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
module Task =
  let map mapping task' =
    task {
      let! value = task'

      return mapping value
    }

  let bind mapping task' =
    task {
      let! value = task'

      return! mapping value
    }

  let taskMap (mapping: 'a -> Task<'b>) task' =
    task {
      let! value = task'

      return! mapping value
    }

  let taskTap (tap: 'a -> Task<unit>) task' =
    task {
      let! value = task'

      do! tap value

      return value
    }

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
      Thumbnailer: ConverterSettings }

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

  type DownloadDocument = string -> string -> Task<string>

  let downloadDocument (bot: ITelegramBotClient) (workersSettings: Settings.WorkersSettings) : DownloadDocument =
    fun id name ->
      task {
        let blobServiceClient = BlobServiceClient(workersSettings.ConnectionString)

        let containerClient =
          blobServiceClient.GetBlobContainerClient(workersSettings.Converter.Input.Container)

        let blobClient = containerClient.GetBlobClient(name)

        use! blobStream = blobClient.OpenWriteAsync(true)

        do! bot.GetInfoAndDownloadFileAsync(id, blobStream) |> Task.map ignore

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

[<RequireQualifiedAccess>]
module Storage =
  let prepareForThumbnailing (workersSettings: Settings.WorkersSettings) =
    fun file ->
      let blobService = BlobServiceClient(workersSettings.ConnectionString)

      let convertedFilesContainer =
        blobService.GetBlobContainerClient(workersSettings.Converter.Output.Container)

      let thumbnailerInputContainer =
        blobService.GetBlobContainerClient(workersSettings.Thumbnailer.Input.Container)

      let convertedFileBlob = convertedFilesContainer.GetBlobClient(file)
      let thumbnailerFileBlob = thumbnailerInputContainer.GetBlobClient(file)

      task {
        let! downloadedBlob = convertedFileBlob.DownloadStreamingAsync()

        do!
          thumbnailerFileBlob.UploadAsync(downloadedBlob.Value.Content, true)
          |> Task.map ignore

        ()
      }

[<RequireQualifiedAccess>]
module Entities =
  [<CLIMutable>]
  type NewConversion =
    { Id: string
      ReceivedMessageId: int
      SentMessageId: int
      UserId: int64 }

  [<CLIMutable>]
  type PreparedConversion =
    { Id: string
      ReceivedMessageId: int
      SentMessageId: int
      UserId: int64
      InputFileName: string }

  [<CLIMutable>]
  type ConvertedConversion =
    { Id: string
      ReceivedMessageId: int
      SentMessageId: int
      UserId: int64
      OutputFileName: string }

[<RequireQualifiedAccess>]
module Database =
  let loadNewConversion (db: IMongoDatabase) : string -> Task<Entities.NewConversion> =
    let collection = db.GetCollection "conversions"

    fun conversionId ->
      task {
        let filter =
          Builders<Entities.NewConversion>.Filter.Eq((fun c -> c.Id), conversionId)

        let! dbConversion = collection.Find(filter).SingleOrDefaultAsync()

        return dbConversion
      }

  let loadPreparedConversion (db: IMongoDatabase) : string -> Task<Entities.PreparedConversion> =
    let collection = db.GetCollection "conversions"

    fun conversionId ->
      task {
        let filter =
          Builders<Entities.PreparedConversion>.Filter.Eq((fun c -> c.Id), conversionId)

        let! dbConversion = collection.Find(filter).SingleOrDefaultAsync()

        return dbConversion
      }

  let saveNewConversion (db: IMongoDatabase) =
    let collection = db.GetCollection "conversions"

    fun conversion -> task { do! collection.InsertOneAsync(conversion) }

  let savePreparedConversion (db: IMongoDatabase) : Entities.PreparedConversion -> Task<unit> =
    let collection = db.GetCollection "conversions"

    fun conversion ->
      task {
        let filter =
          Builders<Entities.PreparedConversion>.Filter.Eq((fun c -> c.Id), conversion.Id)

        do! collection.ReplaceOneAsync(filter, conversion) |> Task.map ignore
      }

  let saveConvertedConversion (db: IMongoDatabase) : Entities.ConvertedConversion -> Task<unit> =
    let collection = db.GetCollection "conversions"

    fun conversion ->
      task {
        let filter =
          Builders<Entities.ConvertedConversion>.Filter.Eq((fun c -> c.Id), conversion.Id)

        do! collection.ReplaceOneAsync(filter, conversion) |> Task.map ignore
      }

  let loadConvertedConversion (db: IMongoDatabase) : string -> Task<Entities.ConvertedConversion> =
    let collection = db.GetCollection "conversions"

    fun conversionId ->
      task {
        let filter =
          Builders<Entities.ConvertedConversion>.Filter.Eq((fun c -> c.Id), conversionId)

        let! dbConversion = collection.Find(filter).SingleOrDefaultAsync()

        return dbConversion
      }

[<RequireQualifiedAccess>]
module HTTP =
  type DownloadLinkError =
    | Unauthorized
    | NotFound
    | ServerError

  type DownloadLink = string -> Task<Result<string, DownloadLinkError>>

  let downloadLink (httpClientFactory: IHttpClientFactory) (workersSettings: Settings.WorkersSettings) : DownloadLink =
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
              use! responseStream = response.Content.ReadAsStreamAsync()

              let blobServiceClient = BlobServiceClient(workersSettings.ConnectionString)

              let uri = Uri(link)

              let fileName = uri.Segments |> Seq.last

              let containerClient =
                blobServiceClient.GetBlobContainerClient(workersSettings.Converter.Input.Container)

              let blobClient = containerClient.GetBlobClient(fileName)

              do! blobClient.UploadAsync(responseStream, true) |> Task.map ignore

              return Ok(fileName)
            }
      }

open Helpers

type Functions
  (
    _workersOptions: IOptions<Settings.WorkersSettings>,
    _bot: ITelegramBotClient,
    _db: IMongoDatabase,
    _httpClientFactory: IHttpClientFactory
  ) =
  let workersSettings = _workersOptions.Value

  [<Function("HandleUpdate")>]
  member this.HandleUpdate([<HttpTrigger("POST", Route = "telegram")>] request: HttpRequest, [<FromBody>] update: Update) : Task<unit> =
    let sendDownloaderMessage = Queue.sendDownloaderMessage workersSettings
    let webmLinkRegex = Regex("https?[^ ]*.webm")

    match update.Type with
    | UpdateType.Message ->
      let userId = update.Message.From.Id
      let sendMessage = Telegram.sendMessage _bot userId
      let replyToMessage = Telegram.replyToMessage _bot userId update.Message.MessageId
      let createConversion = Database.saveNewConversion _db

      match update.Message with
      | Text messageText ->
        match messageText with
        | StartsWith "/start" ->
          sendMessage
            "Send me a video or link to WebM or add bot to group. 🇺🇦 Help the Ukrainian army fight russian and belarus invaders: https://savelife.in.ua/en/donate/"
        | Regex webmLinkRegex matches ->

          let sendUrlToQueue (url: string) =
            task {
              let! sentMessageId = replyToMessage $"File {url} is waiting to be downloaded 🕒"

              let newConversion: Entities.NewConversion =
                { Id = ShortId.Generate()
                  UserId = userId
                  ReceivedMessageId = update.Message.MessageId
                  SentMessageId = sentMessageId }

              do! createConversion newConversion

              let message: Queue.DownloaderMessage =
                { ConversionId = newConversion.Id
                  File = Queue.File.Link url }

              return! sendDownloaderMessage message
            }

          matches |> Seq.map sendUrlToQueue |> Task.WhenAll |> Task.map ignore
        | _ ->
          sendMessage
            "Send me a video or link to WebM or add bot to group. 🇺🇦 Help the Ukrainian army fight russian and belarus invaders: https://savelife.in.ua/en/donate/"
      | Document doc ->
        let sendDocToQueue (doc: Document) =
          task {
            let! sentMessageId = replyToMessage $"File is waiting to be downloaded 🕒"

            let newConversion: Entities.NewConversion =
              { Id = ShortId.Generate()
                UserId = userId
                ReceivedMessageId = update.Message.MessageId
                SentMessageId = sentMessageId }

            do! createConversion newConversion

            let message: Queue.DownloaderMessage =
              { ConversionId = newConversion.Id
                File = Queue.File.Document(doc.FileId, doc.FileName) }

            return! sendDownloaderMessage message
          }

        doc |> sendDocToQueue
      | _ -> Task.FromResult()
    | _ -> Task.FromResult()

  [<Function("Downloader")>]
  member this.DownloadFile
    (
      [<QueueTrigger("%Workers:Downloader:Queue%", Connection = "Workers:ConnectionString")>] message: Queue.DownloaderMessage,
      _: FunctionContext
    ) : Task<unit> =
    let sendConverterMessage = Queue.sendConverterMessage workersSettings
    let loadNewConversion = Database.loadNewConversion _db
    let downloadLink = HTTP.downloadLink _httpClientFactory workersSettings
    let downloadFile = Telegram.downloadDocument _bot workersSettings
    let savePreparedConversion = Database.savePreparedConversion _db

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
              let preparedConversion: Entities.PreparedConversion =
                { Id = message.ConversionId
                  UserId = conversion.UserId
                  ReceivedMessageId = conversion.ReceivedMessageId
                  SentMessageId = conversion.SentMessageId
                  InputFileName = file }

              do! savePreparedConversion preparedConversion

              let converterMessage: Queue.ConverterMessage = { Id = conversion.Id; Name = file }

              do! sendConverterMessage converterMessage

              do! editMessage "Conversion is in progress 🚀"
            }
          | Error(HTTP.DownloadLinkError.Unauthorized) -> editMessage "I am not authorized to download video from this source 🚫"
          | Error(HTTP.DownloadLinkError.NotFound) -> editMessage "Video not found ⚠️"
          | Error(HTTP.DownloadLinkError.ServerError) -> editMessage "Server error 🛑")
    }

  [<Function("Thumbnailer")>]
  member this.GenerateThumbnail
    (
      [<QueueTrigger("%Workers:Converter:Output:Queue%", Connection = "Workers:ConnectionString")>] message: Queue.ConverterResultMessage,
      _: FunctionContext
    ) : Task<unit> =
    let loadConversion = Database.loadPreparedConversion _db
    let sendThumbnailerMessage = Queue.sendTumbnailerMessage workersSettings
    let prepareForThumbnailing = Storage.prepareForThumbnailing workersSettings
    let saveConvertedConversion = Database.saveConvertedConversion _db

    task {
      let! conversion = loadConversion message.Id

      let editMessage =
        Telegram.editMessage _bot conversion.UserId conversion.SentMessageId

      match message.Result with
      | Queue.Success file ->
        do! prepareForThumbnailing file

        let convertedConversion: Entities.ConvertedConversion =
          { Id = conversion.Id
            UserId = conversion.UserId
            OutputFileName = file
            ReceivedMessageId = conversion.ReceivedMessageId
            SentMessageId = conversion.SentMessageId }

        do! saveConvertedConversion convertedConversion

        let converterMessage: Queue.ConverterMessage = { Id = conversion.Id; Name = file }

        do! sendThumbnailerMessage converterMessage
        do! editMessage "Generating thumbnail 🖼️"
      | Queue.Error error ->

        do! editMessage error

      ()
    }

  [<Function("Uploader")>]
  member this.Upload
    (
      [<QueueTrigger("%Workers:Thumbnailer:Output:Queue%", Connection = "Workers:ConnectionString")>] message: Queue.ConverterResultMessage,
      _: FunctionContext
    ) : Task =
    let loadConversion = Database.loadConvertedConversion _db

    task {
      let! conversion = loadConversion message.Id

      let editMessage =
        Telegram.editMessage _bot conversion.UserId conversion.SentMessageId

      let deleteMessage =
        Telegram.deleteMessage _bot conversion.UserId conversion.SentMessageId

      let replyWithVideo =
        Telegram.replyWithVideo workersSettings _bot conversion.UserId conversion.ReceivedMessageId

      return!
        match message.Result with
        | Queue.Success file ->
          task {
            do! deleteMessage ()

            do! replyWithVideo conversion.OutputFileName file
          }
        | Queue.Error error -> task { do! editMessage error }
    }

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

  let private configureMongoClient (options: IOptions<Settings.DatabaseSettings>) (factory: IMongoClientFactory) =
    let settings = options.Value

    factory.GetClient(settings.ConnectionString)

  let private configureMongoDatabase (options: IOptions<Settings.DatabaseSettings>) (mongoClient: IMongoClient) =
    let settings = options.Value

    mongoClient.GetDatabase(settings.Name)

  [<Literal>]
  let chromeUserAgent =
    "Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.102 Safari/537.36"

  let private configureTelegramBotClient (serviceProvider: IServiceProvider) =
    let settings =
      serviceProvider.GetRequiredService<IOptions<Settings.TelegramSettings>>().Value

    let options = TelegramBotClientOptions(settings.Token, settings.ApiUrl)

    options |> TelegramBotClient :> ITelegramBotClient

  let private retryPolicy =
    HttpPolicyExtensions
      .HandleTransientHttpError()
      .WaitAndRetryAsync(5, (fun retryAttempt -> TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))))

  let private configureServices (context: HostBuilderContext) (services: IServiceCollection) =
    services.AddApplicationInsightsTelemetryWorkerService()
    services.ConfigureFunctionsApplicationInsights()

    let cfg = context.Configuration

    services.AddMongoClientFactory()

    services.AddSingleton<IMongoClient>(fun (sp: IServiceProvider) ->
      configureMongoClient (sp.GetRequiredService<IOptions<Settings.DatabaseSettings>>()) (sp.GetRequiredService<IMongoClientFactory>()))

    services.AddSingleton<IMongoDatabase>(fun (sp: IServiceProvider) ->
      configureMongoDatabase (sp.GetRequiredService<IOptions<Settings.DatabaseSettings>>()) (sp.GetRequiredService<IMongoClient>()))

    services.Configure<Settings.WorkersSettings>(cfg.GetSection Settings.WorkersSettings.SectionName)

    services.Configure<Settings.TelegramSettings>(cfg.GetSection(Settings.TelegramSettings.SectionName))
    services.Configure<Settings.DatabaseSettings>(cfg.GetSection(Settings.DatabaseSettings.SectionName))

    services.AddSingleton<ITelegramBotClient>(configureTelegramBotClient)

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
