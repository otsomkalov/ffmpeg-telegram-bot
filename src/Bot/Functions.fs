namespace Bot.Functions

open System.Diagnostics
open System.Threading.Tasks
open Bot.Mappings
open Infrastructure.Queue
open Microsoft.ApplicationInsights
open Microsoft.ApplicationInsights.DataContracts
open Microsoft.AspNetCore.Http
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging
open Telegram
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums
open Telegram.Core
open Domain.Core

type ConverterResultMessage =
  { Id: string; Result: ConversionResult }

type Functions(telemetryClient: TelemetryClient, ffMpegBot: IFFMpegBot, logger: ILogger<Functions>) =

  [<Function("HandleUpdate")>]
  member this.HandleUpdate
    ([<HttpTrigger("POST", Route = "telegram")>] request: HttpRequest, [<FromBody>] update: Update, ctx: FunctionContext) : Task<
                                                                                                                              unit
                                                                                                                             >
    =
    task {
      try
        do! ffMpegBot.ProcessUpdate(update.ToBot())
      with e ->
        logger.LogError(e, "Error during processing an update")
        return ()
    }

  [<Function("Downloader")>]
  member this.Downloader
    (
      [<QueueTrigger("%Workers:Downloader:Queue%", Connection = "Workers:ConnectionString")>] message:
        BaseMessage<DownloaderMessage>,
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
      [<QueueTrigger("%Workers:Converter:Output:Queue%", Connection = "Workers:ConnectionString")>] message:
        BaseMessage<ConverterResultMessage>,
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
      [<QueueTrigger("%Workers:Thumbnailer:Output:Queue%", Connection = "Workers:ConnectionString")>] message:
        BaseMessage<ConverterResultMessage>,
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
      [<QueueTrigger("%Workers:Uploader:Queue%", Connection = "Workers:ConnectionString")>] message:
        BaseMessage<UploaderMessage>,
      _: FunctionContext
    ) : Task =
    let conversionId = message.Data.ConversionId |> ConversionId

    task {
      use activity = (new Activity("Uploader")).SetParentId(message.OperationId)

      use operation = telemetryClient.StartOperation<RequestTelemetry>(activity)

      do! ffMpegBot.UploadConversion conversionId

      operation.Telemetry.Success <- true
    }