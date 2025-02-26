namespace Telegram

open System.Threading.Tasks
open Domain.Core
open Domain.Core.Conversion
open FSharp
open Microsoft.Extensions.Logging
open Microsoft.FSharp.Core
open Telegram.Bot.Types
open Telegram.Core
open otsom.fs.Resources
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
  module Resources =
    let createResourceProvider createDefaultResourceProvider createResourceProvider =
      function
      | Some lang -> createResourceProvider lang
      | None -> createDefaultResourceProvider ()

  [<RequireQualifiedAccess>]
  module User =
    let createResourceProvider (loadUser: User.Load) createDefaultResourceProvider createResourceProvider : User.BuildResourceProvider =
      fun userId ->
        userId
        |> Option.taskMap loadUser
        |> Task.map (Option.bind (Option.bind (_.Lang)))
        |> Task.bind (Resources.createResourceProvider createDefaultResourceProvider createResourceProvider)

  let private processLinks replyToMessage (resp: IResourceProvider) queueUserConversion links =
    let sendUrlToQueue (url: string) =
      task {
        let! sentMessageId = replyToMessage (resp[Resources.LinkDownload, [| url |]])

        return! queueUserConversion sentMessageId (Conversion.New.InputFile.Link { Url = url })
      }

    links |> Seq.map sendUrlToQueue |> Task.WhenAll |> Task.ignore

  let private processDocument replyToMessage (resp: IResourceProvider) queueUserConversion fileId fileName =
    task {
      let! sentMessageId = replyToMessage (resp[Resources.DocumentDownload, [| fileName |]])

      return! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = fileId; Name = fileName })
    }

  let private processVideo replyToMessage (resp: IResourceProvider) queueUserConversion fileId fileName =
    task {
      let! sentMessageId = replyToMessage (resp[Resources.VideoDownload, [| fileName |]])

      return! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = fileId; Name = fileName })
    }

  let private processIncomingMessage parseCommand (resp: IResourceProvider) queueConversion replyToMessage =
    fun message ->
      task {
        let! command = parseCommand message

        return!
          match command with
          | Some(Command.Start) -> replyToMessage (resp[Resources.Welcome]) |> Task.ignore
          | Some(Command.Links links) -> processLinks replyToMessage resp queueConversion links
          | Some(Command.Document(fileId, fileName)) -> processDocument replyToMessage resp queueConversion fileId fileName
          | Some(Command.Video(fileId, fileName)) -> processVideo replyToMessage resp queueConversion fileId fileName
          | None -> Task.FromResult()
      }

  let private processMessageFromNewUser (createUser: User.Create) loadResources queueUserConversion parseCommand replyToMessage =
    fun userId chatId userMessageId (message: Message) ->
      task {
        let user =
          { Id = userId
            Lang = message.From.LanguageCode |> Option.ofObj
            Banned = false }

        do! createUser user

        let! resources = loadResources user.Lang

        return!
          processIncomingMessage parseCommand resources (queueUserConversion userMessageId (Some userId) chatId) replyToMessage message
      }

  let private processMessageFromKnownUser createUserResourceProvider queueUserConversion parseCommand replyToMessage =
    fun (user: User) userMessageId chatId message ->
      task {
        let! translations = createUserResourceProvider user.Lang

        return!
          processIncomingMessage parseCommand translations (queueUserConversion userMessageId (Some user.Id) chatId) replyToMessage message
      }

  let processPrivateMessage
    (replyToUserMessage: ReplyToUserMessage)
    (loadUser: User.Load)
    (createUser: User.Create)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    (createResourceProvider: CreateResourceProvider)
    createDefaultResourceProvider
    : ProcessPrivateMessage =
    let createResourceProvider =
      Resources.createResourceProvider createDefaultResourceProvider createResourceProvider

    fun message ->
      let userId = message.From.Id |> UserId
      let replyToMessage = replyToUserMessage userId message.MessageId
      let userMessageId = message.MessageId |> UserMessageId

      let processMessageFromKnownUser =
        processMessageFromKnownUser createResourceProvider queueUserConversion parseCommand replyToMessage

      let processMessageFromNewUser =
        processMessageFromNewUser createUser createResourceProvider queueUserConversion parseCommand replyToMessage

      Logf.logfi logger "Processing private message from user %i{UserId}" (userId |> UserId.value)

      task {
        let! user = loadUser userId

        match user with
        | Some u when u.Banned ->
          let! resp = createResourceProvider u.Lang

          do! replyToMessage (resp[Resources.UserBan]) |> Task.ignore
        | Some u -> do! processMessageFromKnownUser u userMessageId userId message
        | None -> do! processMessageFromNewUser userId userId userMessageId message
      }

  let processGroupMessage
    (replyToUserMessage: ReplyToUserMessage)
    (buildDefaultResourceProvider: CreateDefaultResourceProvider)
    (loadUser: User.Load)
    (createUser: User.Create)
    (loadGroup: Group.Load)
    (saveGroup: Group.Save)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    (createResourceProvider: CreateResourceProvider)
    createDefaultResourceProvider
    : ProcessGroupMessage =
    let createResourceProvider =
      Resources.createResourceProvider createDefaultResourceProvider createResourceProvider

    fun message ->
      let groupId = message.Chat.Id |> GroupId
      let groupId' = message.Chat.Id |> UserId
      let userId = message.From.Id |> UserId
      let replyToMessage = replyToUserMessage groupId' message.MessageId
      let userMessageId = message.MessageId |> UserMessageId

      let processMessageFromKnownUser =
        processMessageFromKnownUser createResourceProvider queueUserConversion parseCommand replyToMessage

      let processMessageFromNewUser =
        processMessageFromNewUser createUser createResourceProvider queueUserConversion parseCommand replyToMessage

      Logf.logfi logger "Processing message from user %i{UserId} in group %i{ChatId}" (userId |> UserId.value) (groupId |> GroupId.value)

      task {
        let! user = loadUser userId
        let! group = loadGroup groupId

        return!
          match user, group with
          | _, Some g when g.Banned ->
            task {
              let! resp = createDefaultResourceProvider ()
              do! replyToMessage (resp[Resources.GroupBan]) |> Task.ignore
            }
          | Some u, _ when u.Banned ->
            task {
              let! resp = createResourceProvider u.Lang

              do! replyToMessage (resp[Resources.UserBan]) |> Task.ignore
            }
          | Some u, Some g -> processMessageFromKnownUser u userMessageId groupId' message
          | Some u, None ->
            task {
              do! saveGroup { Id = groupId; Banned = false }

              return! processMessageFromKnownUser u userMessageId groupId' message
            }
          | None, Some g -> processMessageFromNewUser userId groupId' userMessageId message
          | _ ->
            task {
              do! saveGroup { Id = groupId; Banned = false }

              return! processMessageFromNewUser userId groupId' userMessageId message
            }
      }

  let processChannelPost
    (replyToUserMessage: ReplyToUserMessage)
    (loadChannel: Channel.Load)
    (saveChannel: Channel.Save)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    (createDefaultResourceProvider: CreateDefaultResourceProvider)
    : ProcessChannelPost =
    fun post ->
      let channelId = post.Chat.Id |> ChannelId.create
      let chatId = post.Chat.Id |> UserId
      let replyToMessage = replyToUserMessage chatId post.MessageId
      let postId = (post.MessageId |> UserMessageId)
      let queueConversion = (queueUserConversion postId None chatId)

      Logf.logfi logger "Processing post from channel %i{ChannelId}" (channelId |> ChannelId.value)

      task {
        let! resp = createDefaultResourceProvider ()
        let! channel = loadChannel channelId

        return!
          match channel with
          | Some c when c.Banned -> replyToMessage (resp[Resources.ChannelBan]) |> Task.ignore
          | Some _ -> processIncomingMessage parseCommand resp queueConversion replyToMessage post
          | None ->
            task {
              do! saveChannel { Id = channelId; Banned = false }

              return! processIncomingMessage parseCommand resp queueConversion replyToMessage post
            }
      }

  let downloadFileAndQueueConversion
    (editBotMessage: EditBotMessage)
    (loadUserConversion: UserConversion.Load)
    (loadUser: User.Load)
    (prepareConversion: Conversion.New.Prepare)
    (createResourceProvider: CreateResourceProvider)
    createDefaultResourceProvider
    : DownloadFileAndQueueConversion =
    let onSuccess editMessage (resp: IResourceProvider) =
      fun _ -> editMessage (resp[Resources.ConversionInProgress])

    let onError editMessage (resp: IResourceProvider) =
      fun error ->
        match error with
        | New.DownloadLinkError.Unauthorized -> editMessage (resp[Resources.NotAuthorized])
        | New.DownloadLinkError.NotFound -> editMessage (resp[Resources.NotFound])
        | New.DownloadLinkError.ServerError -> editMessage (resp[Resources.ServerError])

    fun conversionId file ->
      task {
        let! userConversion = loadUserConversion conversionId

        let! resp =
          userConversion.UserId
          |> (User.createResourceProvider loadUser createDefaultResourceProvider createResourceProvider)

        let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

        let onSuccess = (onSuccess editMessage resp)
        let onError = (onError editMessage resp)

        return! prepareConversion conversionId file |> TaskResult.taskEither onSuccess onError
      }

  let processConversionResult
    (loadUserConversion: UserConversion.Load)
    (editBotMessage: EditBotMessage)
    (loadConversion: Conversion.Load)
    loadUser
    (saveVideo: Conversion.Prepared.SaveVideo)
    (complete: Conversion.Thumbnailed.Complete)
    (queueUpload: Conversion.Completed.QueueUpload)
    (createResourceProvider: CreateResourceProvider)
    createDefaultResourceProvider
    : ProcessConversionResult =

    let processResult editMessage (resp: IResourceProvider) conversion =
      function
      | ConversionResult.Success file ->
        match conversion with
        | Prepared preparedConversion ->
          saveVideo preparedConversion file
          |> Task.bind (fun _ -> editMessage (resp[Resources.VideoConverted]))
        | Thumbnailed thumbnailedConversion ->
          complete thumbnailedConversion file
          |> Task.bind queueUpload
          |> Task.bind (fun _ -> editMessage (resp[Resources.Uploading]))
      | ConversionResult.Error error -> editMessage error

    fun conversionId result ->
      task {
        let! userConversion = loadUserConversion conversionId

        let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

        let! resp =
          userConversion.UserId
          |> (User.createResourceProvider loadUser createDefaultResourceProvider createResourceProvider)

        let! conversion = loadConversion conversionId

        return! processResult editMessage resp conversion result
      }

  let processThumbnailingResult
    (loadUserConversion: UserConversion.Load)
    (editBotMessage: EditBotMessage)
    (loadConversion: Conversion.Load)
    loadUser
    (saveThumbnail: Conversion.Prepared.SaveThumbnail)
    (complete: Conversion.Converted.Complete)
    (queueUpload: Conversion.Completed.QueueUpload)
    (createResourceProvider: CreateResourceProvider)
    createDefaultResourceProvider
    : ProcessThumbnailingResult =

    let processResult editMessage (resp: IResourceProvider) conversion =
      function
      | ConversionResult.Success file ->
        match conversion with
        | Prepared preparedConversion ->
          saveThumbnail preparedConversion file
          |> Task.bind (fun _ -> editMessage (resp[Resources.ThumbnailGenerated]))
        | Converted convertedConversion ->
          complete convertedConversion file
          |> Task.bind queueUpload
          |> Task.bind (fun _ -> editMessage (resp[Resources.Uploading]))
      | ConversionResult.Error error -> editMessage error

    fun conversionId result ->
      task {
        let! userConversion = loadUserConversion conversionId

        let editMessage = editBotMessage userConversion.ChatId userConversion.SentMessageId

        let! resp =
          userConversion.UserId
          |> (User.createResourceProvider loadUser createDefaultResourceProvider createResourceProvider)

        let! conversion = loadConversion conversionId

        return! processResult editMessage resp conversion result
      }

  let uploadCompletedConversion
    (loadUserConversion: UserConversion.Load)
    (loadConversion: Conversion.Load)
    (deleteBotMessage: DeleteBotMessage)
    (replyWithVideo: ReplyWithVideo)
    loadUser
    (cleanupConversion: Conversion.Completed.Cleanup)
    createResourceProvider
    crea
    : UploadCompletedConversion =
    let uploadAndClean userConversion =
      function
      | Completed conversion ->
        task {
          let! resp =
            userConversion.UserId
            |> (User.createResourceProvider loadUser loadResources loadDefaultResources)

          do!
            replyWithVideo
              userConversion.ChatId
              userConversion.ReceivedMessageId
              (tran Resources.Completed)
              conversion.OutputFile
              conversion.ThumbnailFile

          do! cleanupConversion conversion
          do! deleteBotMessage userConversion.ChatId userConversion.SentMessageId
        }

    fun id ->
      task {
        let! userConversion = loadUserConversion id
        let! conversion = loadConversion id

        return! uploadAndClean userConversion conversion
      }