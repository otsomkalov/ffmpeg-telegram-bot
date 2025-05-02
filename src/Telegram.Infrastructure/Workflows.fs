namespace Telegram.Infrastructure

open System.IO
open System.Text.RegularExpressions
open Azure.Storage.Blobs
open Domain.Core
open Infrastructure.Settings
open Telegram.Bot
open Telegram.Core
open Telegram.Bot.Types
open Telegram.Infrastructure.Settings
open Telegram.Workflows
open otsom.fs.Extensions
open otsom.fs.Telegram.Bot.Core
open System.Threading.Tasks
open Telegram.Infrastructure.Helpers
open otsom.fs.Extensions.String

module Workflows =
  let deleteBotMessage (bot: ITelegramBotClient) : DeleteBotMessage =
    fun userId messageId -> bot.DeleteMessageAsync((userId |> UserId.value |> ChatId), (messageId |> BotMessageId.value))

  let replyWithVideo (workersSettings: WorkersSettings) (bot: ITelegramBotClient) : ReplyWithVideo =
    let blobServiceClient = BlobServiceClient(workersSettings.ConnectionString)

    fun userId messageId ->
      fun text video thumbnail ->
        let (Conversion.Video video) = video
        let (Conversion.Thumbnail thumbnail) = thumbnail

        let videoContainer =
          blobServiceClient.GetBlobContainerClient workersSettings.Converter.Output.Container

        let thumbnailContainer =
          blobServiceClient.GetBlobContainerClient workersSettings.Thumbnailer.Output.Container

        let videoBlob = videoContainer.GetBlobClient(video)
        let thumbnailBlob = thumbnailContainer.GetBlobClient(thumbnail)

        [ videoBlob.DownloadStreamingAsync(); thumbnailBlob.DownloadStreamingAsync() ]
        |> Task.WhenAll
        |> Task.bind (fun [| videoStreamResponse; thumbnailStreamResponse |] ->
          bot.SendVideoAsync(
            (userId |> UserId.value |> ChatId),
            InputFileStream(videoStreamResponse.Value.Content, video),
            caption = text,
            replyToMessageId = messageId.Value,
            thumbnail = InputFileStream(thumbnailStreamResponse.Value.Content, thumbnail),
            disableNotification = true
          ))
        |> Task.ignore

  let parseCommand (settings: InputValidationSettings) : ParseCommand =
    let linkRegex = Regex(settings.LinkRegex)

    fun message ->
      match message with
      | FromBot -> None |> Task.FromResult
      | Text messageText ->
        match messageText with
        | StartsWith "/start" -> Command.Start |> Some |> Task.FromResult
        | Regex linkRegex matches -> matches |> Command.Links |> Some |> Task.FromResult
        | _ -> None |> Task.FromResult
      | Document settings.MimeTypes doc -> Command.Document(doc.FileId, doc.FileName) |> Some |> Task.FromResult
      | Video settings.MimeTypes vid ->
        let videoName =
          vid.FileName
          |> Option.ofObj
          |> Option.defaultWith(fun _ ->
            let tmpFile = Path.GetTempFileName()
            let fileInfo = FileInfo(tmpFile)

            fileInfo.Name)

        Command.Video(vid.FileId, videoName) |> Some |> Task.FromResult
      | _ -> None |> Task.FromResult