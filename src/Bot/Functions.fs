namespace Bot.Functions

open System.Diagnostics
open System.Threading.Tasks
open Domain
open Domain.Workflows
open FSharp
open Infrastructure.Core
open Infrastructure.Queue
open Microsoft.ApplicationInsights
open Microsoft.ApplicationInsights.DataContracts
open Microsoft.AspNetCore.Http
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Telegram
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums
open Telegram.Core
open Telegram.Workflows
open Domain.Core
open Telegram.Repos
open otsom.fs.Resources

type ConverterResultMessage =
  { Id: string; Result: ConversionResult }

type Functions
  (
    queueConversionPreparation: Conversion.New.QueuePreparation,
    parseCommand: ParseCommand,
    createConversion: Conversion.Create,
    telemetryClient: TelemetryClient,
    userRepo: IUserRepo,
    channelRepo: IChannelRepo,
    groupRepo: IGroupRepo,
    userConversionRepo: IUserConversionRepo,
    conversionRepo: IConversionRepo,
    conversionService: IConversionService,
    loadResources: Resources.LoadResources,
    loadUserResources: User.LoadResources,
    createDefaultResourceProvider: CreateDefaultResourceProvider,
    buildBotService: BuildExtendedBotService
  ) =

  [<Function("HandleUpdate")>]
  member this.HandleUpdate
    ([<HttpTrigger("POST", Route = "telegram")>] request: HttpRequest, [<FromBody>] update: Update, ctx: FunctionContext)
    : Task<unit> =
    let logger = nameof (this.HandleUpdate) |> ctx.GetLogger

    let queueUserConversion =
      UserConversion.queueProcessing createConversion userConversionRepo queueConversionPreparation

    let processPrivateMessage =
      processPrivateMessage loadResources userRepo queueUserConversion parseCommand logger buildBotService

    let processGroupMessage =
      processGroupMessage loadResources userRepo groupRepo queueUserConversion parseCommand logger buildBotService

    let processChannelPost =
      processChannelPost createDefaultResourceProvider channelRepo queueUserConversion parseCommand logger buildBotService

    task {
      try
        let processUpdateTask =
          match update.Type with
          | UpdateType.Message when update.Message.From.Id = update.Message.Chat.Id -> processPrivateMessage update.Message
          | UpdateType.Message -> processGroupMessage update.Message
          | UpdateType.ChannelPost -> processChannelPost update.ChannelPost
          | _ -> Task.FromResult()

        do! processUpdateTask
      with e ->
        Logf.elogfe logger e "Error during processing an update:"
        return ()
    }

  [<Function("Downloader")>]
  member this.DownloadFile
    (
      [<QueueTrigger("%Workers:Downloader:Queue%", Connection = "Workers:ConnectionString")>] message: BaseMessage<DownloaderMessage>,
      _: FunctionContext
    ) : Task<unit> =
    let data = message.Data

    let downloadFileAndQueueConversion =
      downloadFileAndQueueConversion userConversionRepo loadUserResources conversionService buildBotService

    task {
      use activity = (new Activity("Downloader")).SetParentId(message.OperationId)
      use operation = telemetryClient.StartOperation<RequestTelemetry>(activity)

      do! downloadFileAndQueueConversion data.ConversionId data.File

      operation.Telemetry.Success <- true
    }

  [<Function("SaveConversionResult")>]
  member this.SaveConversionResult
    (
      [<QueueTrigger("%Workers:Converter:Output:Queue%", Connection = "Workers:ConnectionString")>] message:
        BaseMessage<ConverterResultMessage>,
      _: FunctionContext
    ) : Task<unit> =
    let processConversionResult =
      processConversionResult userConversionRepo conversionRepo loadUserResources conversionService buildBotService

    task {
      use activity =
        (new Activity("SaveConversionResult")).SetParentId(message.OperationId)

      use operation = telemetryClient.StartOperation<RequestTelemetry>(activity)

      let data = message.Data

      do! processConversionResult (ConversionId data.Id) data.Result

      operation.Telemetry.Success <- true
    }

  [<Function("SaveThumbnailingResult")>]
  member this.SaveThumbnailingResult
    (
      [<QueueTrigger("%Workers:Thumbnailer:Output:Queue%", Connection = "Workers:ConnectionString")>] message:
        BaseMessage<ConverterResultMessage>,
      _: FunctionContext
    ) : Task<unit> =
    let processThumbnailingResult =
      processThumbnailingResult userConversionRepo conversionRepo loadUserResources conversionService buildBotService

    task {
      use activity =
        (new Activity("SaveThumbnailingResult")).SetParentId(message.OperationId)

      use operation = telemetryClient.StartOperation<RequestTelemetry>(activity)

      let data = message.Data

      do! processThumbnailingResult (ConversionId data.Id) data.Result

      operation.Telemetry.Success <- true
    }

  [<Function("Uploader")>]
  member this.Upload
    (
      [<QueueTrigger("%Workers:Uploader:Queue%", Connection = "Workers:ConnectionString")>] message: BaseMessage<UploaderMessage>,
      _: FunctionContext
    ) : Task =
    let conversionId = message.Data.ConversionId |> ConversionId

    let uploadSuccessfulConversion =
      uploadCompletedConversion userConversionRepo conversionRepo loadUserResources conversionService buildBotService

    task {
      use activity = (new Activity("Uploader")).SetParentId(message.OperationId)

      use operation = telemetryClient.StartOperation<RequestTelemetry>(activity)

      do! uploadSuccessfulConversion conversionId

      operation.Telemetry.Success <- true
    }