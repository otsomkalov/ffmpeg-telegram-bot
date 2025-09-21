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
    parseCommand: ParseCommand,
    createConversion: Conversion.Create,
    telemetryClient: TelemetryClient,
    userConversionRepo: IUserConversionRepo,
    conversionRepo: IConversionRepo,
    createResourceProvider: CreateResourceProvider,
    buildBotService: BuildExtendedBotService,
    ffMpegBot: IFFMpegBot,
    chatRepo: IChatRepo,
    chatSvc: IChatSvc
  ) =

  [<Function("HandleUpdate")>]
  member this.HandleUpdate
    ([<HttpTrigger("POST", Route = "telegram")>] request: HttpRequest, [<FromBody>] update: Update, ctx: FunctionContext)
    : Task<unit> =
    let logger = nameof this.HandleUpdate |> ctx.GetLogger

    let queueUserConversion =
      UserConversion.queueProcessing createConversion userConversionRepo conversionRepo

    let processPrivateMessage =
      processPrivateMessage chatRepo chatSvc queueUserConversion parseCommand logger createResourceProvider buildBotService

    let processGroupMessage =
      processGroupMessage chatRepo chatSvc queueUserConversion parseCommand logger createResourceProvider buildBotService

    let processChannelPost =
      processChannelPost chatRepo chatSvc queueUserConversion parseCommand logger createResourceProvider buildBotService

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
  member this.Downloader
    (
      [<QueueTrigger("%Workers:Downloader:Queue%", Connection = "Workers:ConnectionString")>]
      message: BaseMessage<DownloaderMessage>,
      _: FunctionContext
    ) : Task<unit> =
    let data = message.Data

    task {
      use activity = (new Activity("Downloader")).SetParentId(message.OperationId)
      use operation = telemetryClient.StartOperation<RequestTelemetry>(activity)

      do! ffMpegBot.PrepareConversion(data.ConversionId, data.File)

      operation.Telemetry.Success <- true
    }

  [<Function("Converter")>]
  member this.Converter
    (
      [<QueueTrigger("%Workers:Converter:Output:Queue%", Connection = "Workers:ConnectionString")>]
      message: BaseMessage<ConverterResultMessage>,
      _: FunctionContext
    ) : Task<unit> =
    let data = message.Data

    task {
      use activity = (new Activity("Converter")).SetParentId(message.OperationId)

      use operation = telemetryClient.StartOperation<RequestTelemetry>(activity)

      do! ffMpegBot.SaveVideo(ConversionId data.Id, data.Result)

      operation.Telemetry.Success <- true
    }

  [<Function("Thumbnailer")>]
  member this.Thumbnailer
    (
      [<QueueTrigger("%Workers:Thumbnailer:Output:Queue%", Connection = "Workers:ConnectionString")>]
      message: BaseMessage<ConverterResultMessage>,
      _: FunctionContext
    ) : Task<unit> =
    let data = message.Data

    task {
      use activity = (new Activity("Thumbnailer")).SetParentId(message.OperationId)

      use operation = telemetryClient.StartOperation<RequestTelemetry>(activity)

      do! ffMpegBot.SaveThumbnail(ConversionId data.Id, data.Result)

      operation.Telemetry.Success <- true
    }

  [<Function("Uploader")>]
  member this.Uploader
    (
      [<QueueTrigger("%Workers:Uploader:Queue%", Connection = "Workers:ConnectionString")>]
      message: BaseMessage<UploaderMessage>,
      _: FunctionContext
    ) : Task =
    let conversionId = message.Data.ConversionId |> ConversionId

    task {
      use activity = (new Activity("Uploader")).SetParentId(message.OperationId)

      use operation = telemetryClient.StartOperation<RequestTelemetry>(activity)

      do! ffMpegBot.UploadConversion conversionId

      operation.Telemetry.Success <- true
    }