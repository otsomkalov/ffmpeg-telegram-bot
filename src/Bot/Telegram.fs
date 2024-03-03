[<RequireQualifiedAccess>]
module Bot.Telegram

open System.Text.RegularExpressions
open System.Threading.Tasks
open Azure.Storage.Blobs
open Telegram.Bot
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums
open otsom.FSharp.Extensions
open shortid

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

let replyWithVideo (workersSettings: Settings.WorkersSettings) (bot: ITelegramBotClient) (userId: int64) (messageId: int) : ReplyWithVideo =
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

      do! bot.GetInfoAndDownloadFileAsync(id, converterBlobStream) |> Task.map ignore

      use! thumbnailerBlobStream = getBlobStream workersSettings name Thumbnailer

      do! bot.GetInfoAndDownloadFileAsync(id, thumbnailerBlobStream) |> Task.map ignore

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