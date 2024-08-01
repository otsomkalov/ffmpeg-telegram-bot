namespace Telegram

open System.Threading.Tasks
open Domain.Core
open Domain.Core.Conversion
open Telegram.Core
open otsom.fs.Telegram.Bot.Core
open otsom.fs.Extensions
open Domain.Repos
open Telegram.Repos

module Workflows =
  type DeleteBotMessage = UserId -> BotMessageId -> Task
  type ReplyWithVideo = UserId -> UserMessageId -> string -> Conversion.Video -> Conversion.Thumbnail -> Task<unit>

  [<RequireQualifiedAccess>]
  module UserConversion =
    let queueProcessing
      (createConversion: Conversion.Create)
      (saveUserConversion: UserConversion.Save)
      (queueConversionPreparation: Conversion.New.QueuePreparation)
      : UserConversion.QueueProcessing =
      fun userMessageId userId chatId sentMessageId inputFile ->
        task {
          let! conversion = createConversion ()

          do!
            saveUserConversion
              { ChatId = chatId
                UserId = userId
                SentMessageId = sentMessageId
                ReceivedMessageId = userMessageId
                ConversionId = conversion.Id }

          return! queueConversionPreparation conversion.Id inputFile
        }

  [<RequireQualifiedAccess>]
  module User =
    let loadTranslations
      (loadUser: User.Load)
      (loadTranslations: Translation.LoadTranslations)
      (loadDefaultTranslations: Translation.LoadDefaultTranslations)
      : User.LoadTranslations =
      Option.taskMap
        (loadUser
        >> Task.bind (fun user ->
          loadTranslations user.Lang))
      >> Task.bind (Option.defaultWithTask loadDefaultTranslations)

  let processMessage
    (sendUserMessage: SendUserMessage)
    (replyToUserMessage: ReplyToUserMessage)
    (getLocaleTranslations: Translation.LoadTranslations)
    (ensureUserExists: User.EnsureExists)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    : ProcessMessage =
    fun message ->
      let chatId = message.Chat.Id |> UserId
      let userId = message.From |> Option.ofObj |> Option.map (_.Id >> UserId)
      let sendMessage = sendUserMessage chatId
      let replyToMessage = replyToUserMessage chatId message.MessageId

      let processLinks (_, tranf: Translation.FormatTranslation) (queueUserConversion) links =
        let sendUrlToQueue (url: string) =
          task {
            let! sentMessageId = replyToMessage (tranf (Resources.LinkDownload, [| url |]))

            return! queueUserConversion sentMessageId (Conversion.New.InputFile.Link { Url = url })
          }

        links |> Seq.map sendUrlToQueue |> Task.WhenAll |> Task.ignore

      let processDocument (_, tranf: Translation.FormatTranslation) queueUserConversion fileId fileName =
        task {
          let! sentMessageId = replyToMessage (tranf (Resources.DocumentDownload, [| fileName |]))

          return! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = fileId; Name = fileName })
        }

      let processVideo (_, tranf: Translation.FormatTranslation) queueUserConversion fileId fileName =
        task {
          let! sentMessageId = replyToMessage (tranf (Resources.VideoDownload, [| fileName |]))

          return! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = fileId; Name = fileName })
        }

      let processCommand =
        fun cmd ->
          task {
            let! tran, tranf =
              message.From
              |> Option.ofObj
              |> Option.bind (_.LanguageCode >> Option.ofObj)
              |> getLocaleTranslations

            return!
              match cmd with
              | Command.Start -> sendMessage (tran Resources.Welcome)
              | Command.Links links ->
                processLinks (tran, tranf) (queueUserConversion (message.MessageId |> UserMessageId) userId chatId) links
              | Command.Document(fileId, fileName) ->
                processDocument (tran, tranf) (queueUserConversion (message.MessageId |> UserMessageId) userId chatId) fileId fileName
              | Command.Video(fileId, fileName) ->
                processVideo (tran, tranf) (queueUserConversion (message.MessageId |> UserMessageId) userId chatId) fileId fileName
          }

      let processMessage' =
        function
        | None -> Task.FromResult()
        | Some cmd ->
          match message.From |> Option.ofObj with
          | Some sender ->
            task {
              do! ensureUserExists (Mappings.User.fromTg sender)
              do! processCommand cmd
            }
          | None -> processCommand cmd

      parseCommand message |> Task.bind processMessage'

  let downloadFileAndQueueConversion
    (editBotMessage: EditBotMessage)
    (loadUserConversion: UserConversion.Load)
    (loadTranslations: User.LoadTranslations)
    (prepareConversion: Conversion.New.Prepare)
    : DownloadFileAndQueueConversion =

    let onSuccess editMessage tran =
      fun _ -> editMessage (tran Resources.ConversionInProgress)

    let onError editMessage tran =
      fun error ->
        match error with
        | New.DownloadLinkError.Unauthorized -> editMessage (tran Resources.NotAuthorized)
        | New.DownloadLinkError.NotFound -> editMessage (tran Resources.NotFound)
        | New.DownloadLinkError.ServerError -> editMessage (tran Resources.ServerError)

    fun conversionId file ->
      task {
        let! userConversion = loadUserConversion conversionId

        let! tran, _ = userConversion.UserId |> loadTranslations

        let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

        let onSuccess = (onSuccess editMessage tran)
        let onError = (onError editMessage tran)

        return! prepareConversion conversionId file |> TaskResult.taskEither onSuccess onError
      }

  let processConversionResult
    (loadUserConversion: UserConversion.Load)
    (editBotMessage: EditBotMessage)
    (loadConversion: Conversion.Load)
    (loadTranslations: User.LoadTranslations)
    (saveVideo: Conversion.Prepared.SaveVideo)
    (complete: Conversion.Thumbnailed.Complete)
    (queueUpload: Conversion.Completed.QueueUpload)
    : ProcessConversionResult =

    let processResult editMessage tran conversion =
      function
      | ConversionResult.Success file ->
        match conversion with
        | Prepared preparedConversion ->
          saveVideo preparedConversion file
          |> Task.bind (fun _ -> editMessage (tran Resources.VideoConverted))
        | Thumbnailed thumbnailedConversion ->
          complete thumbnailedConversion file
          |> Task.bind queueUpload
          |> Task.bind (fun _ -> editMessage (tran Resources.Uploading))
      | ConversionResult.Error error -> editMessage error

    fun conversionId result ->
      task {
        let! userConversion = loadUserConversion conversionId

        let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

        let! tran, _ = userConversion.UserId |> loadTranslations

        let! conversion = loadConversion conversionId

        return! processResult editMessage tran conversion result
      }

  let processThumbnailingResult
    (loadUserConversion: UserConversion.Load)
    (editBotMessage: EditBotMessage)
    (loadConversion: Conversion.Load)
    (loadTranslations: User.LoadTranslations)
    (saveThumbnail: Conversion.Prepared.SaveThumbnail)
    (complete: Conversion.Converted.Complete)
    (queueUpload: Conversion.Completed.QueueUpload)
    : ProcessThumbnailingResult =

    let processResult editMessage tran conversion =
      function
      | ConversionResult.Success file ->
        match conversion with
        | Prepared preparedConversion ->
          saveThumbnail preparedConversion file
          |> Task.bind (fun _ -> editMessage (tran Resources.ThumbnailGenerated))
        | Converted convertedConversion ->
          complete convertedConversion file
          |> Task.bind queueUpload
          |> Task.bind (fun _ -> editMessage (tran Resources.Uploading))
      | ConversionResult.Error error -> editMessage error

    fun conversionId result ->
      task {
        let! userConversion = loadUserConversion conversionId

        let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

        let! tran, _ = userConversion.UserId |> loadTranslations

        let! conversion = loadConversion conversionId

        return! processResult editMessage tran conversion result
      }

  let uploadCompletedConversion
    (loadUserConversion: UserConversion.Load)
    (loadConversion: Conversion.Load)
    (deleteBotMessage: DeleteBotMessage)
    (replyWithVideo: ReplyWithVideo)
    (loadTranslations: User.LoadTranslations)
    (cleanupConversion: Conversion.Completed.Cleanup)
    : UploadCompletedConversion =
    let uploadAndClean userConversion =
      function
      | Completed conversion ->
        task {
          let! tran, _ = userConversion.UserId |> loadTranslations

          do! replyWithVideo userConversion.ChatId userConversion.ReceivedMessageId (tran Resources.Completed) conversion.OutputFile conversion.ThumbnailFile

          do! cleanupConversion conversion
          do! deleteBotMessage userConversion.ChatId userConversion.SentMessageId
        }

    fun id ->
      task {
        let! userConversion = loadUserConversion id
        let! conversion = loadConversion id

        return! uploadAndClean userConversion conversion
      }
