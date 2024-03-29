[<RequireQualifiedAccess>]
module Bot.Telegram

open System.Threading.Tasks
open Azure.Storage.Blobs
open Infrastructure.Settings
open Telegram.Bot
open otsom.fs.Extensions

type BlobType =
  | Converter
  | Thumbnailer

let getBlobClient (workersSettings: WorkersSettings) =
  fun name type' ->
    let blobServiceClient = BlobServiceClient(workersSettings.ConnectionString)

    let container =
      match type' with
      | Converter -> workersSettings.Converter.Input.Container
      | Thumbnailer -> workersSettings.Thumbnailer.Input.Container

    let containerClient = blobServiceClient.GetBlobContainerClient(container)

    containerClient.GetBlobClient(name)

let getBlobStream (workersSettings: WorkersSettings) =
  fun name type' ->
    let blobClient = getBlobClient workersSettings name type'

    blobClient.OpenWriteAsync(true)

type DownloadDocument = string -> string -> Task<string>

let downloadDocument (bot: ITelegramBotClient) (workersSettings: WorkersSettings) : DownloadDocument =
  fun id name ->
    task {
      use! converterBlobStream = getBlobStream workersSettings name Converter

      do! bot.GetInfoAndDownloadFileAsync(id, converterBlobStream) |> Task.ignore

      use! thumbnailerBlobStream = getBlobStream workersSettings name Thumbnailer

      do! bot.GetInfoAndDownloadFileAsync(id, thumbnailerBlobStream) |> Task.ignore

      return name
    }