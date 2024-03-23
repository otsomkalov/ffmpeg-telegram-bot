namespace Telegram

open System.Threading.Tasks
open Domain.Core
open Domain.Workflows
open Telegram.Core
open otsom.fs.Telegram.Bot.Core

module Workflows =
  type DeleteBotMessage = UserId -> BotMessageId -> Task
  type ReplyWithVideo = UserId -> UserMessageId -> Video -> Thumbnail -> Task<unit>

  [<RequireQualifiedAccess>]
  module UserConversion =
    type Load = ConversionId -> Task<UserConversion>

  let uploadCompletedConversion
    (loadUserConversion: UserConversion.Load)
    (loadCompletedConversion: Conversion.Completed.Load)
    (deleteBotMessage: DeleteBotMessage)
    (replyWithVideo: ReplyWithVideo)
    (deleteVideo: Conversion.Completed.DeleteVideo)
    (deleteThumbnail: Conversion.Completed.DeleteThumbnail)
    : UploadCompletedConversion =
    fun id ->
      task {
        let! userConversion = loadUserConversion id
        let! conversion = loadCompletedConversion id

        do! replyWithVideo userConversion.ChatId userConversion.ReceivedMessageId conversion.OutputFile conversion.ThumbnailFile

        do! deleteVideo conversion.OutputFile
        do! deleteThumbnail conversion.ThumbnailFile
        do! deleteBotMessage userConversion.ChatId userConversion.SentMessageId
      }
