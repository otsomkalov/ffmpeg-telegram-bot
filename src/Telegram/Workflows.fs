namespace Telegram

open System.Threading.Tasks
open Domain.Core
open Domain.Core.Conversion
open FSharp
open Microsoft.Extensions.Logging
open Microsoft.FSharp.Core
open Telegram.Bot.Types
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
      (repo: #ISaveUserConversion)
      (queueConversionPreparation: Conversion.New.QueuePreparation)
      : UserConversion.QueueProcessing =
      fun userMessageId userId chatId sentMessageId inputFile ->
        task {
          let! conversion = createConversion ()

          do!
            repo.SaveUserConversion
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
      (repo: #ILoadUser)
      (loadTranslations: Translation.LoadTranslations)
      (loadDefaultTranslations: Translation.LoadDefaultTranslations)
      : User.LoadTranslations =
      Option.taskMap
        (repo.LoadUser
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

  let private processIncomingMessage parseCommand (tran, tranf) queueConversion replyToMessage =
    fun message ->
      task{
          let! command = parseCommand message

          return!
            match command with
            | Some(Command.Start) -> replyToMessage (tran Resources.Welcome) |> Task.ignore
            | Some(Command.Links links) -> processLinks replyToMessage tranf queueConversion links
            | Some(Command.Document(fileId, fileName)) -> processDocument replyToMessage tranf queueConversion fileId fileName
            | Some(Command.Video(fileId, fileName)) -> processVideo replyToMessage tranf queueConversion fileId fileName
            | None -> Task.FromResult()
        }

  let private processMessageFromNewUser (repo: #ISaveUser) (getLocaleTranslations: Translation.LoadTranslations) queueUserConversion parseCommand replyToMessage =
    fun userId chatId userMessageId (message: Message) ->
      task {
        let user = {Id = userId; Lang = message.From.LanguageCode |> Option.ofObj; Banned = false }

        do! repo.SaveUser user

        let! translations = getLocaleTranslations user.Lang

        return!
          processIncomingMessage
            parseCommand
            translations
            (queueUserConversion userMessageId (Some userId) chatId)
            replyToMessage
            message
      }

  let private processMessageFromKnownUser getLocaleTranslations queueUserConversion parseCommand replyToMessage =
    fun user userMessageId chatId message ->
      task {
        let! translations = getLocaleTranslations user.Lang

        return!
          processIncomingMessage
            parseCommand
            translations
            (queueUserConversion userMessageId (Some user.Id) chatId)
            replyToMessage
            message
      }

  let processPrivateMessage
    (replyToUserMessage: ReplyToUserMessage)
    (getLocaleTranslations: Translation.LoadTranslations)
    (userRepo: #ILoadUser)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    : ProcessPrivateMessage =
    fun message ->
      let userId = message.From.Id |> UserId
      let replyToMessage = replyToUserMessage userId message.MessageId
      let userMessageId = message.MessageId |> UserMessageId
      let processMessageFromKnownUser = processMessageFromKnownUser getLocaleTranslations queueUserConversion parseCommand replyToMessage
      let processMessageFromNewUser = processMessageFromNewUser userRepo getLocaleTranslations queueUserConversion parseCommand replyToMessage

      Logf.logfi logger "Processing private message from user %i{UserId}" (userId |> UserId.value)

      task {
        let! user = userRepo.LoadUser userId

        return!
          match user with
          | Some u when u.Banned ->
            task {
              let! tran, _ = getLocaleTranslations u.Lang

              do! replyToMessage (tran Resources.UserBan) |> Task.ignore
            }
          | Some u ->
            processMessageFromKnownUser u userMessageId userId message
          | None ->
            processMessageFromNewUser userId userId userMessageId message
      }

  let processGroupMessage
    (replyToUserMessage: ReplyToUserMessage)
    (getLocaleTranslations: Translation.LoadTranslations)
    (loadDefaultTranslations: Translation.LoadDefaultTranslations)
    (userRepo: #ILoadUser)
    (groupRepo: #ILoadGroup & #ISaveGroup)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    : ProcessGroupMessage =
    fun message ->
      let groupId = message.Chat.Id |> GroupId
      let groupId' = message.Chat.Id |> UserId
      let userId = message.From.Id |> UserId
      let replyToMessage = replyToUserMessage groupId' message.MessageId
      let userMessageId = message.MessageId |> UserMessageId
      let processMessageFromKnownUser = processMessageFromKnownUser getLocaleTranslations queueUserConversion parseCommand replyToMessage
      let processMessageFromNewUser = processMessageFromNewUser userRepo getLocaleTranslations queueUserConversion parseCommand replyToMessage

      Logf.logfi logger "Processing message from user %i{UserId} in group %i{ChatId}" (userId |> UserId.value) (groupId |> GroupId.value)

      task {
        let! user = userRepo.LoadUser userId
        let! group = groupRepo.LoadGroup groupId

        return!
          match user, group with
          | _, Some g when g.Banned ->
            task {
              let! tran, _ = loadDefaultTranslations ()
              do! replyToMessage (tran Resources.GroupBan) |> Task.ignore
            }
          | Some u, _ when u.Banned ->
            task{
              let! tran, _ = getLocaleTranslations u.Lang

              do! replyToMessage (tran Resources.UserBan) |> Task.ignore
            }
          | Some u, Some g ->
            processMessageFromKnownUser u userMessageId groupId' message
          | Some u, None ->
            task {
              do! groupRepo.SaveGroup {Id = groupId; Banned = false }

              return!
                processMessageFromKnownUser u userMessageId groupId' message
            }
          | None, Some g ->
            processMessageFromNewUser userId groupId' userMessageId message
          | _ ->
            task {
              do! groupRepo.SaveGroup {Id = groupId; Banned = false }

              return! processMessageFromNewUser userId groupId' userMessageId message
            }
      }

  let processChannelPost
    (replyToUserMessage: ReplyToUserMessage)
    (loadDefaultTranslations: Translation.LoadDefaultTranslations)
    (channelRepo: #ILoadChannel & #ISaveChannel)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    : ProcessChannelPost =
    fun post ->
      let channelId = post.Chat.Id |> ChannelId.create
      let chatId = post.Chat.Id |> UserId
      let replyToMessage = replyToUserMessage chatId post.MessageId
      let postId = (post.MessageId |> UserMessageId)
      let queueConversion = (queueUserConversion postId None chatId)

      Logf.logfi logger "Processing post from channel %i{ChannelId}" (channelId |> ChannelId.value)

      task {
        let! tran, tranf = loadDefaultTranslations ()
        let! channel = channelRepo.LoadChannel channelId

        return!
          match channel with
          | Some c when c.Banned -> replyToMessage (tran Resources.ChannelBan) |> Task.ignore
          | Some _ ->
            processIncomingMessage parseCommand (tran, tranf) queueConversion replyToMessage post
          | None ->
            task {
              do! channelRepo.SaveChannel { Id = channelId; Banned = false }

              return!
                processIncomingMessage parseCommand (tran, tranf) queueConversion replyToMessage post
            }
      }

  let downloadFileAndQueueConversion
    (editBotMessage: EditBotMessage)
    (userConversionRepo: #ILoadUserConversion)
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
        let! userConversion = userConversionRepo.LoadUserConversion conversionId

        let! tran, _ = userConversion.UserId |> loadTranslations

        let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

        let onSuccess = (onSuccess editMessage tran)
        let onError = (onError editMessage tran)

        return! prepareConversion conversionId file |> TaskResult.taskEither onSuccess onError
      }

  let processConversionResult
    (userConversionRepo: #ILoadUserConversion)
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
        let! userConversion = userConversionRepo.LoadUserConversion conversionId

        let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

        let! tran, _ = userConversion.UserId |> loadTranslations

        let! conversion = loadConversion conversionId

        return! processResult editMessage tran conversion result
      }

  let processThumbnailingResult
    (userConversionRepo: #ILoadUserConversion)
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
        let! userConversion = userConversionRepo.LoadUserConversion conversionId

        let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

        let! tran, _ = userConversion.UserId |> loadTranslations

        let! conversion = loadConversion conversionId

        return! processResult editMessage tran conversion result
      }

  let uploadCompletedConversion
    (userConversionRepo: #ILoadUserConversion)
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
        let! userConversion = userConversionRepo.LoadUserConversion id
        let! conversion = loadConversion id

        return! uploadAndClean userConversion conversion
      }
