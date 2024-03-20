namespace Telegram.Infrastructure

open Azure.Storage.Blobs
open Infrastructure.Settings
open Telegram.Bot
open Telegram.Bot.Types
open Telegram.Workflows
open Telegram.Infrastructure.Core
open otsom.fs.Extensions
open otsom.fs.Telegram.Bot.Core
open System.Threading.Tasks

module Workflows =
  let deleteBotMessage (bot: ITelegramBotClient) : DeleteBotMessage =
    fun userId messageId -> task { do! bot.DeleteMessageAsync((userId |> UserId.value |> ChatId), (messageId |> BotMessageId.value)) }

  let replyWithVideo (workersSettings: WorkersSettings) (bot: ITelegramBotClient) : ReplyWithVideo =
    let blobServiceClient = BlobServiceClient(workersSettings.ConnectionString)

    fun userId messageId ->
      fun video thumbnail ->
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
            caption = "🇺🇦 Help the Ukrainian army fight russian and belarus invaders: https://savelife.in.ua/en/donate/",
            replyToMessageId = (messageId |> UserMessageId.value),
            thumbnail = InputFileStream(thumbnailStreamResponse.Value.Content, thumbnail),
            disableNotification = true
          ))
        |> Task.ignore
