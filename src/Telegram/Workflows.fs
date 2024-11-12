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
open otsom.fs

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
  module internal Resources =
    let loadResources
      (loadResources: Resources.LoadResources)
      (loadDefaultResources: Resources.LoadDefaultResources) =
        function
        | Some l -> loadResources l
        | None -> loadDefaultResources()

  [<RequireQualifiedAccess>]
  module User =
    let loadResources
      (loadUser: User.Load)
      loadResources
      loadDefaultResources =
        function
        | Some id ->
          id
          |> loadUser
          &|> (Option.bind (_.Lang))
          |> Task.bind (Resources.loadResources loadResources loadDefaultResources)
        | None -> loadDefaultResources()

  let private processLinks replyToMessage (resf: Resources.FormatResource) queueUserConversion links =
      let sendUrlToQueue (url: string) =
        task {
          let! sentMessageId = replyToMessage (resf Resources.LinkDownload [ url ])

          return! queueUserConversion sentMessageId (Conversion.New.InputFile.Link { Url = url })
        }

      links |> Seq.map sendUrlToQueue |> Task.WhenAll |> Task.ignore

  let private processDocument replyToMessage (resf: Resources.FormatResource) queueUserConversion fileId fileName =
      task {
        let! sentMessageId = replyToMessage (resf Resources.DocumentDownload [ fileName ])

        return! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = fileId; Name = fileName })
      }

  let private processVideo replyToMessage (tranf: Resources.FormatResource) queueUserConversion fileId fileName =
      task {
        let! sentMessageId = replyToMessage (tranf Resources.VideoDownload [ fileName ])

        return! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = fileId; Name = fileName })
      }

  let private processIncomingMessage parseCommand (res, resf) queueConversion replyToMessage =
    fun message ->
      task{
          let! command = parseCommand message

          return!
            match command with
            | Some(Command.Start) -> replyToMessage (res Resources.Welcome) |> Task.ignore
            | Some(Command.Links links) -> processLinks replyToMessage resf queueConversion links
            | Some(Command.Document(fileId, fileName)) -> processDocument replyToMessage resf queueConversion fileId fileName
            | Some(Command.Video(fileId, fileName)) -> processVideo replyToMessage resf queueConversion fileId fileName
            | None -> Task.FromResult()
        }

  let private processMessageFromNewUser (createUser: User.Create) loadResources queueUserConversion parseCommand replyToMessage =
    fun userId chatId userMessageId (message: Message) ->
      task {
        let user = {Id = userId; Lang = message.From.LanguageCode |> Option.ofObj; Banned = false }

        do! createUser user

        let! resources = loadResources user.Lang

        return!
          processIncomingMessage
            parseCommand
            resources
            (queueUserConversion userMessageId (Some userId) chatId)
            replyToMessage
            message
      }

  let private processMessageFromKnownUser loadResources queueUserConversion parseCommand replyToMessage =
    fun (user: User) userMessageId chatId message ->
      task {
        let! translations = loadResources (Some user.Id)

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
    loadResources
    loadDefaultResources
    (loadUser: User.Load)
    (createUser: User.Create)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    : ProcessPrivateMessage =
    fun message ->
      let userId = message.From.Id |> UserId
      let replyToMessage = replyToUserMessage userId message.MessageId
      let userMessageId = message.MessageId |> UserMessageId
      let loadResources' = Resources.loadResources loadResources loadDefaultResources
      let processMessageFromKnownUser = processMessageFromKnownUser (User.loadResources loadUser loadResources loadDefaultResources) queueUserConversion parseCommand replyToMessage
      let processMessageFromNewUser = processMessageFromNewUser createUser loadResources' queueUserConversion parseCommand replyToMessage

      Logf.logfi logger "Processing private message from user %i{UserId}" (userId |> UserId.value)

      task {
        let! user = loadUser userId

        return!
          match user with
          | Some u when u.Banned ->
            task {
              let! tran, _ = loadResources' u.Lang

              do! replyToMessage (tran Resources.UserBan) |> Task.ignore
            }
          | Some u ->
            processMessageFromKnownUser u userMessageId userId message
          | None ->
            processMessageFromNewUser userId userId userMessageId message
      }

  let processGroupMessage
    (replyToUserMessage: ReplyToUserMessage)
    loadResources
    (loadDefaultResources: Resources.LoadDefaultResources)
    (loadUser: User.Load)
    (createUser: User.Create)
    (loadGroup: Group.Load)
    (saveGroup: Group.Save)
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
      let loadResources' = Resources.loadResources loadResources loadDefaultResources
      let processMessageFromKnownUser = processMessageFromKnownUser (User.loadResources loadUser loadResources loadDefaultResources) queueUserConversion parseCommand replyToMessage
      let processMessageFromNewUser = processMessageFromNewUser createUser loadResources' queueUserConversion parseCommand replyToMessage

      Logf.logfi logger "Processing message from user %i{UserId} in group %i{ChatId}" (userId |> UserId.value) (groupId |> GroupId.value)

      task {
        let! user = loadUser userId
        let! group = loadGroup groupId

        return!
          match user, group with
          | _, Some g when g.Banned ->
            task {
              let! tran, _ = loadDefaultResources ()
              do! replyToMessage (tran Resources.GroupBan) |> Task.ignore
            }
          | Some u, _ when u.Banned ->
            task{
              let! tran, _ = loadResources' u.Lang

              do! replyToMessage (tran Resources.UserBan) |> Task.ignore
            }
          | Some u, Some g ->
            processMessageFromKnownUser u userMessageId groupId' message
          | Some u, None ->
            task {
              do! saveGroup {Id = groupId; Banned = false }

              return!
                processMessageFromKnownUser u userMessageId groupId' message
            }
          | None, Some g ->
            processMessageFromNewUser userId groupId' userMessageId message
          | _ ->
            task {
              do! saveGroup {Id = groupId; Banned = false }

              return! processMessageFromNewUser userId groupId' userMessageId message
            }
      }

  let processChannelPost
    (replyToUserMessage: ReplyToUserMessage)
    (loadDefaultResources: Resources.LoadDefaultResources)
    (loadChannel: Channel.Load)
    (saveChannel: Channel.Save)
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
        let! res, resf = loadDefaultResources ()
        let! channel = loadChannel channelId

        return!
          match channel with
          | Some c when c.Banned -> replyToMessage (res Resources.ChannelBan) |> Task.ignore
          | Some _ ->
            processIncomingMessage parseCommand (res, resf) queueConversion replyToMessage post
          | None ->
            task {
              do! saveChannel { Id = channelId; Banned = false }

              return!
                processIncomingMessage parseCommand (res, resf) queueConversion replyToMessage post
            }
      }

  let downloadFileAndQueueConversion
    (editBotMessage: EditBotMessage)
    (loadUserConversion: UserConversion.Load)
    loadUser
    loadResources
    loadDefaultResources
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

        let! tran, _ = userConversion.UserId |> (User.loadResources loadUser loadResources loadDefaultResources)

        let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

        let onSuccess = (onSuccess editMessage tran)
        let onError = (onError editMessage tran)

        return! prepareConversion conversionId file |> TaskResult.taskEither onSuccess onError
      }

  let processConversionResult
    (loadUserConversion: UserConversion.Load)
    (editBotMessage: EditBotMessage)
    (loadConversion: Conversion.Load)
    loadUser
    (loadResources: Resources.LoadResources)
    loadDefaultResources
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

        let! tran, _ = userConversion.UserId |> (User.loadResources loadUser loadResources loadDefaultResources)

        let! conversion = loadConversion conversionId

        return! processResult editMessage tran conversion result
      }

  let processThumbnailingResult
    (loadUserConversion: UserConversion.Load)
    (editBotMessage: EditBotMessage)
    (loadConversion: Conversion.Load)
    loadUser
    (loadResources: Resources.LoadResources)
    (loadDefaultResources)
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

        let! tran, _ = userConversion.UserId |> (User.loadResources loadUser loadResources loadDefaultResources)

        let! conversion = loadConversion conversionId

        return! processResult editMessage tran conversion result
      }

  let uploadCompletedConversion
    (loadUserConversion: UserConversion.Load)
    (loadConversion: Conversion.Load)
    (deleteBotMessage: DeleteBotMessage)
    (replyWithVideo: ReplyWithVideo)
    loadUser
    (loadResources)
    loadDefaultResources
    (cleanupConversion: Conversion.Completed.Cleanup)
    : UploadCompletedConversion =
    let uploadAndClean userConversion =
      function
      | Completed conversion ->
        task {
          let! tran, _ = userConversion.UserId |> (User.loadResources loadUser loadResources loadDefaultResources)

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
