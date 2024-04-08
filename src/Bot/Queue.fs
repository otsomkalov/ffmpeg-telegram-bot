[<RequireQualifiedAccess>]
module Bot.Queue

open Azure.Storage.Queues
open FSharp
open Infrastructure.Helpers
open Infrastructure.Settings
open Telegram.Core
open Telegram.Infrastructure
open otsom.fs.Extensions
open Domain.Core

[<CLIMutable>]
type DownloaderMessage =
  { ConversionId: ConversionId
    File: Conversion.New.InputFile }

let sendDownloaderMessage (workersSettings: WorkersSettings) logger =
  fun (message: DownloaderMessage) ->
    Logf.logfi logger "Sending queue message to downloader"
    let queueServiceClient = QueueServiceClient(workersSettings.ConnectionString)

    let queueClient =
      queueServiceClient.GetQueueClient(workersSettings.Downloader.Queue)

    let messageBody = JSON.serialize message

    queueClient.SendMessageAsync(messageBody) |> Task.ignore

type ConverterResultMessage =
  { Id: string; Result: ConversionResult }
