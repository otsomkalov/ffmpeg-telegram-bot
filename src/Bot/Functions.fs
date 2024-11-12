namespace Bot.Functions

open System.Diagnostics
open System.Threading.Tasks
open Domain.Workflows
open FSharp
open Infrastructure.Core
open Infrastructure.Queue
open Infrastructure.Settings
open Microsoft.ApplicationInsights
open Microsoft.ApplicationInsights.DataContracts
open Microsoft.AspNetCore.Http
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums
open Telegram.Core
open Telegram.Workflows
open otsom.fs.Telegram.Bot.Core
open Domain.Repos
open Domain.Core
open Telegram.Repos
open otsom.fs.Resources

type ConverterResultMessage =
  { Id: string; Result: ConversionResult }

type Functions
  (
    workersSettings: WorkersSettings,
    replyToUserMessage: ReplyToUserMessage,
    editBotMessage: EditBotMessage,
    loadUserConversion: UserConversion.Load,
    deleteBotMessage: DeleteBotMessage,
    replyWithVideo: ReplyWithVideo,
    completeThumbnailedConversion: Conversion.Thumbnailed.Complete,
    completeConvertedConversion: Conversion.Converted.Complete,
    saveVideo: Conversion.Prepared.SaveVideo,
    saveThumbnail: Conversion.Prepared.SaveThumbnail,
    downloadLink: Conversion.New.InputFile.DownloadLink,
    downloadDocument: Conversion.New.InputFile.DownloadDocument,
    saveUserConversion: UserConversion.Save,
    queueConversionPreparation: Conversion.New.QueuePreparation,
    parseCommand: ParseCommand,
    createConversion: Conversion.Create,
    loadConversion: Conversion.Load,
    saveConversion: Conversion.Save,
    telemetryClient: TelemetryClient,
    cleanupConversion: Conversion.Completed.Cleanup,
    loadUser: User.Load,
    loadChannel: Channel.Load,
    createUser: User.Create,
    saveChannel: Channel.Save,
    loadGroup: Group.Load,
    saveGroup: Group.Save,
    loadResources: LoadResources,
    loadDefaultResources: LoadDefaultResources
  ) =

  [<Function("HandleUpdate")>]
  member this.HandleUpdate([<HttpTrigger("POST", Route = "telegram")>] request: HttpRequest, [<FromBody>] update: Update, ctx: FunctionContext) : Task<unit> =
    let logger = nameof(this.HandleUpdate) |> ctx.GetLogger

    let queueUserConversion =
      UserConversion.queueProcessing createConversion saveUserConversion queueConversionPreparation

    let processPrivateMessage =
      processPrivateMessage replyToUserMessage loadResources loadDefaultResources loadUser createUser queueUserConversion parseCommand logger

    let processGeoupMessage =
      processGroupMessage replyToUserMessage loadResources loadDefaultResources loadUser createUser loadGroup saveGroup queueUserConversion parseCommand logger

    let processChannelPost =
      processChannelPost replyToUserMessage loadDefaultResources loadChannel saveChannel queueUserConversion parseCommand logger

    task {
      try
        let processUpdateTask =
          match update.Type with
          | UpdateType.Message when update.Message.From.Id = update.Message.Chat.Id -> processPrivateMessage update.Message
          | UpdateType.Message -> processGeoupMessage update.Message
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
    let queueConversion = Conversion.Prepared.queueConversion workersSettings message.OperationId
    let queueThumbnailing = Conversion.Prepared.queueThumbnailing workersSettings message.OperationId

    let prepareConversion =
      Conversion.New.prepare downloadLink downloadDocument saveConversion queueConversion queueThumbnailing

    let downloadFileAndQueueConversion =
      downloadFileAndQueueConversion editBotMessage loadUserConversion loadUser loadResources loadDefaultResources prepareConversion

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
      processConversionResult
        loadUserConversion
        editBotMessage
        loadConversion
        loadUser
        loadResources
        loadDefaultResources
        saveVideo
        completeThumbnailedConversion
        (Conversion.Completed.queueUpload workersSettings message.OperationId)

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
      processThumbnailingResult
        loadUserConversion
        editBotMessage
        loadConversion
        loadUser
        loadResources
        loadDefaultResources
        saveThumbnail
        completeConvertedConversion
        (Conversion.Completed.queueUpload workersSettings message.OperationId)

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
      uploadCompletedConversion loadUserConversion loadConversion deleteBotMessage replyWithVideo loadUser loadResources loadDefaultResources cleanupConversion

    task {
      use activity =
        (new Activity("Uploader")).SetParentId(message.OperationId)

      use operation = telemetryClient.StartOperation<RequestTelemetry>(activity)

      do! uploadSuccessfulConversion conversionId

      operation.Telemetry.Success <- true
    }
