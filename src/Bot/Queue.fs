[<RequireQualifiedAccess>]
module Bot.Queue

open Azure.Storage.Queues
open Bot.Helpers
open otsom.FSharp.Extensions

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
