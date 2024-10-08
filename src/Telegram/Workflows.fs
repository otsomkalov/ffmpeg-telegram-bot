namespace Telegram

open System.Threading.Tasks
open Domain.Core
open Domain.Core.Conversion
open FSharp
open Microsoft.Extensions.Logging
open Microsoft.FSharp.Core
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
        >> Task.map Option.get
        >> Task.bind (fun user ->
          loadTranslations user.Lang))
      >> Task.bind (Option.defaultWithTask loadDefaultTranslations)

  let private processLinks replyToMessage (tranf: Translation.FormatTranslation) queueUserConversion links =
      let sendUrlToQueue (url: string) =
        task {
          let! sentMessageId = replyToMessage (tranf (Resources.LinkDownload, [| url |]))

          return! queueUserConversion sentMessageId (Conversion.New.InputFile.Link { Url = url })
        }

      links |> Seq.map sendUrlToQueue |> Task.WhenAll |> Task.ignore

  let private processDocument replyToMessage (tranf: Translation.FormatTranslation) queueUserConversion fileId fileName =
      task {
        let! sentMessageId = replyToMessage (tranf (Resources.DocumentDownload, [| fileName |]))

        return! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = fileId; Name = fileName })
      }

  let private processVideo replyToMessage (tranf: Translation.FormatTranslation) queueUserConversion fileId fileName =
      task {
        let! sentMessageId = replyToMessage (tranf (Resources.VideoDownload, [| fileName |]))

        return! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = fileId; Name = fileName })
      }

  let private processIncomingMessage parseCommand (tran, tranf) queueUserConversion sendMessage replyToMessage =
    fun userMessageId userId chatId message ->
      task{
          let! command = parseCommand message

          let queueConversion = queueUserConversion userMessageId userId chatId

          return!
            match command with
            | Some(Command.Start) -> sendMessage (tran Resources.Welcome)
            | Some(Command.Links links) -> processLinks replyToMessage tranf queueConversion links
            | Some(Command.Document(fileId, fileName)) -> processDocument replyToMessage tranf queueConversion fileId fileName
            | Some(Command.Video(fileId, fileName)) -> processVideo replyToMessage tranf queueConversion fileId fileName
            | None -> Task.FromResult()
        }

  let processMessage
    (sendUserMessage: SendUserMessage)
    (replyToUserMessage: ReplyToUserMessage)
    (getLocaleTranslations: Translation.LoadTranslations)
    (loadUser: User.Load)
    (createUser: User.Create)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    : ProcessMessage =
    fun message ->
      let chatId = message.Chat.Id |> UserId
      let userId = message.From.Id |> UserId
      let sendMessage = sendUserMessage chatId
      let replyToMessage = replyToUserMessage chatId message.MessageId
      let userMessageId = message.MessageId |> UserMessageId

      Logf.logfi logger "Processing message from user %i{UserId} and chat %i{ChatId}" (userId |> UserId.value) (chatId |> UserId.value)

      userId
      |> loadUser
      |> Task.bind (Option.defaultWithTask (fun () -> createUser userId (message.From.LanguageCode |> Option.ofObj)))
      |> Task.bind (fun user ->
        task {
          let! translations = getLocaleTranslations user.Lang

          return!
            processIncomingMessage
              parseCommand
              translations
              queueUserConversion
              sendMessage
              replyToMessage
              userMessageId
              (Some userId)
              chatId
              message
        })

  let processPost
    (sendUserMessage: SendUserMessage)
    (replyToUserMessage: ReplyToUserMessage)
    (loadDefaultTranslations: Translation.LoadDefaultTranslations)
    (loadChannel: Channel.Load)
    (createChannel: Channel.Create)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    : ProcessPost =
    fun post ->
      let channelId = post.Chat.Id |> ChannelId.create
      let chatId = post.Chat.Id |> UserId
      let sendMessage = sendUserMessage chatId
      let replyToMessage = replyToUserMessage chatId post.MessageId
      let postId = (post.MessageId |> UserMessageId)

      Logf.logfi logger "Processing post from channel %i{ChannelId}" (channelId |> ChannelId.value)

      channelId
      |> loadChannel
      |> Task.bind (Option.defaultWithTask (fun () -> createChannel channelId))
      |> Task.bind (fun channel ->
        task {
          let! translations = loadDefaultTranslations ()

          return! processIncomingMessage parseCommand translations queueUserConversion sendMessage replyToMessage postId None chatId post
        })

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
