namespace Bot.Functions

open System.Diagnostics
open System.Threading.Tasks
open Bot
open Bot.Mappings
open Infrastructure.Queue
open Microsoft.AspNetCore.Http
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging
open Telegram
open Telegram.Bot.Types
open Telegram.Core
open Domain.Core

type ConverterResultMessage =
  { Id: string; Result: ConversionResult }

type Functions(ffMpegBot: IFFMpegBot, logger: ILogger<Functions>) =

  [<Function("HandleUpdate")>]
  member this.HandleUpdate
    ([<HttpTrigger("POST", Route = "telegram")>] request: HttpRequest, [<FromBody>] update: Update, ctx: FunctionContext) : Task<
                                                                                                                              unit
                                                                                                                             >
    =
    task {
      use activity =
        Observability.ActivitySource.StartActivity("HandleUpdate", ActivityKind.Internal)

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
      ctx: FunctionContext
    ) : Task<unit> =
    let data = message.Data

    task {
      use activity =
        Observability.ActivitySource.StartActivity(
          "Downloader",
          ActivityKind.Consumer,
          Activity.Current.Context,
          links = [ ActivityLink(ActivityContext.Parse(message.Context["traceparent"], null)) ]
        )

      do! ffMpegBot.PrepareConversion(data.ConversionId, data.File)
    }

  [<Function("Converter")>]
  member this.Converter
    (
      [<QueueTrigger("%Workers:Converter:Output:Queue%", Connection = "Workers:ConnectionString")>] message:
        BaseMessage<ConverterResultMessage>,
      ctx: FunctionContext
    ) : Task<unit> =
    let data = message.Data

    task {
      use activity =
        Observability.ActivitySource.StartActivity(
          "Converter",
          ActivityKind.Consumer,
          Activity.Current.Context,
          links = [ ActivityLink(ActivityContext.Parse(message.Context["traceparent"], null)) ]
        )

      do! ffMpegBot.SaveVideo(ConversionId data.Id, data.Result)
    }

  [<Function("Thumbnailer")>]
  member this.Thumbnailer
    (
      [<QueueTrigger("%Workers:Thumbnailer:Output:Queue%", Connection = "Workers:ConnectionString")>] message:
        BaseMessage<ConverterResultMessage>,
      ctx: FunctionContext
    ) : Task<unit> =
    let data = message.Data

    task {
      use activity =
        Observability.ActivitySource.StartActivity(
          "Thumbnailer",
          ActivityKind.Consumer,
          Activity.Current.Context,
          links = [ ActivityLink(ActivityContext.Parse(message.Context["traceparent"], null)) ]
        )

      do! ffMpegBot.SaveThumbnail(ConversionId data.Id, data.Result)
    }

  [<Function("Uploader")>]
  member this.Uploader
    (
      [<QueueTrigger("%Workers:Uploader:Queue%", Connection = "Workers:ConnectionString")>] message:
        BaseMessage<UploaderMessage>,
      ctx: FunctionContext
    ) : Task =
    let conversionId = message.Data.ConversionId |> ConversionId

    task {
      use activity =
        Observability.ActivitySource.StartActivity(
          "Uploader",
          ActivityKind.Consumer,
          Activity.Current.Context,
          links = [ ActivityLink(ActivityContext.Parse(message.Context["traceparent"], null)) ]
        )

      do! ffMpegBot.UploadConversion conversionId
    }