[<RequireQualifiedAccess>]
module Bot.Telegram

open System.Threading.Tasks
open Azure.Storage.Blobs
open Telegram.Bot
open Telegram.Bot.Types
open otsom.fs.Extensions
open otsom.fs.Telegram.Bot.Core

type DeleteMessage = unit -> Task<unit>

let deleteMessage (bot: ITelegramBotClient) (userId: UserId) messageId : DeleteMessage =
  fun () -> task { do! bot.DeleteMessageAsync((userId |> UserId.value |> ChatId), (messageId |> BotMessageId.value)) }

type ReplyWithVideo = string -> string -> Task<unit>

let replyWithVideo (workersSettings: Settings.WorkersSettings) (bot: ITelegramBotClient) (userId: UserId) (messageId: int) : ReplyWithVideo =
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
          (userId |> UserId.value |> ChatId),
          InputFileStream(videoStreamResponse.Value.Content, video),
          caption = "🇺🇦 Help the Ukrainian army fight russian and belarus invaders: https://savelife.in.ua/en/donate/",
          replyToMessageId = messageId,
          thumbnail = InputFileStream(thumbnailStreamResponse.Value.Content, thumbnail),
          disableNotification = true
        )
        |> Task.ignore

      do! videoBlob.DeleteAsync() |> Task.ignore
      do! thumbnailBlob.DeleteAsync() |> Task.ignore
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

      do! bot.GetInfoAndDownloadFileAsync(id, converterBlobStream) |> Task.ignore

      use! thumbnailerBlobStream = getBlobStream workersSettings name Thumbnailer

      do! bot.GetInfoAndDownloadFileAsync(id, thumbnailerBlobStream) |> Task.ignore

      return name
    }

let inline sendDocToQueue replyToMessage saveConversion saveUserConversion sendDownloaderMessage =
  fun userId (message: Message) (doc: ^T when ^T:(member FileName: string)  and 'T:(member FileId: string)) ->
    task {
      let! sentMessageId = replyToMessage $"File *{doc.FileName}* is waiting to be downloaded 🕒"

      let newConversion: Domain.Conversion.New = { Id = ShortId.Generate() }

      do! saveConversion newConversion

      let userConversion: Domain.UserConversion =
        { ConversionId = newConversion.Id
          UserId = userId
          SentMessageId = sentMessageId
          ReceivedMessageId = message.MessageId }

      do! saveUserConversion userConversion

      let message: Queue.DownloaderMessage =
        { ConversionId = newConversion.Id
          File = Queue.File.Document(doc.FileId, doc.FileName) }

      return! sendDownloaderMessage message
    }