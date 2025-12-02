namespace Telegram.Infrastructure.Services

open System.Threading.Tasks
open Azure.Storage.Blobs
open Domain.Core
open Infrastructure.Settings
open Microsoft.Extensions.Options
open Telegram
open Telegram.Bot
open Telegram.Bot.Types
open otsom.fs.Bot
open otsom.fs.Bot.Telegram
open FsToolkit.ErrorHandling

type ExtendedBotService(bot: ITelegramBotClient, workersOptions: IOptions<WorkersSettings>, chatId: ChatId) =
  inherit BotService(bot, chatId)

  let workersSettings = workersOptions.Value

  interface IExtendedBotService with
    member this.ReplyWithVideo(messageId, text, video, thumbnail) =
      let blobServiceClient = BlobServiceClient(workersSettings.ConnectionString)

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
        bot.SendVideo(
          (chatId.Value |> Types.ChatId),
          InputFileStream(videoStreamResponse.Value.Content, video),
          caption = text,
          replyParameters = messageId.Value,
          thumbnail = InputFileStream(thumbnailStreamResponse.Value.Content, thumbnail),
          disableNotification = true
        ))
      |> Task.ignore