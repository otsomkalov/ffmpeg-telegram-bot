namespace Telegram

open System.Threading.Tasks
open Domain.Core
open Domain.Workflows
open Telegram.Core
open otsom.fs.Telegram.Bot.Core
open otsom.fs.Extensions

module Workflows =
  type DeleteBotMessage = UserId -> BotMessageId -> Task
  type ReplyWithVideo = UserId -> UserMessageId -> Conversion.Video -> Conversion.Thumbnail -> Task<unit>

  [<RequireQualifiedAccess>]
  module UserConversion =
    type Load = ConversionId -> Task<UserConversion>

  [<RequireQualifiedAccess>]
  module User =
    type Load = UserId -> Task<User>

  let processThumbnailingResult
    (loadUserConversion: UserConversion.Load)
    (editBotMessage: EditBotMessage)
    (loadPreparedOrConverted: Conversion.PreparedOrConverted.Load)
    (loadUser: User.Load)
    (getLocaleTranslations: GetLocaleTranslations)
    (saveThumbnail: Conversion.Prepared.SaveThumbnail)
    (complete: Conversion.Converted.Complete)
    (queueUpload: Conversion.Completed.QueueUpload)
    : ProcessThumbnailingResult =

    let processResult editMessage tran conversion =
      function
      | ConversionResult.Success file ->
        match conversion with
        | Choice1Of2 preparedConversion ->
          saveThumbnail preparedConversion file
          |> Task.bind (fun _ -> editMessage (tran Resources.ThumbnailGenerated))
        | Choice2Of2 convertedConversion ->
          complete convertedConversion file
          |> Task.bind queueUpload
          |> Task.bind (fun _ -> editMessage (tran Resources.Uploading))
      | ConversionResult.Error error -> editMessage error

    fun conversionId result ->
      task {
        let! userConversion = loadUserConversion conversionId

        let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

        let! tran, _ =
          userConversion.UserId
          |> Option.taskMap loadUser
          |> Task.map (Option.bind (_.Lang))
          |> Task.bind getLocaleTranslations

        let! conversion = loadPreparedOrConverted conversionId

        return! processResult editMessage tran conversion result
      }

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
