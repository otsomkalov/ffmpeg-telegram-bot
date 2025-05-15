namespace Telegram

open System.Threading.Tasks
open Domain
open Domain.Core
open Domain.Core.Conversion
open FSharp
open Microsoft.Extensions.Logging
open Microsoft.FSharp.Core
open Telegram.Bot.Types
open Telegram.Core
open Telegram.Helpers
open otsom.fs.Bot
open otsom.fs.Resources
open otsom.fs.Extensions
open Telegram.Repos

module Workflows =
  [<RequireQualifiedAccess>]
  module UserConversion =
    let queueProcessing
      (createConversion: Create)
      (repo: #ISaveUserConversion)
      (conversionRepo: #IQueuePreparation)
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

          return! conversionRepo.QueuePreparation(conversion.Id, inputFile)
        }

  [<RequireQualifiedAccess>]
  module Resources =
    let loadResources
      (loadResources: CreateResourceProvider)
      (loadDefaultResources: CreateDefaultResourceProvider)
      : Resources.LoadResources =
      function
      | Some l -> loadResources l
      | None -> loadDefaultResources ()

  [<RequireQualifiedAccess>]
  module User =
    let loadResources (repo: #ILoadUser) (loadResources: Resources.LoadResources) : User.LoadResources =
      function
      | Some id -> repo.LoadUser id |> Task.bind (Option.bind _.Lang >> loadResources)
      | None -> loadResources None

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

        match command with
        | Some(Command.Start) ->
          do! replyToMessage (resp[Resources.Welcome]) |> Task.ignore
        | Some(Command.Links links) ->
          do! processLinks replyToMessage resp queueConversion links
        | Some(Command.Document(fileId, fileName)) ->
          do! processDocument replyToMessage resp queueConversion fileId fileName
        | Some(Command.Video(fileId, fileName)) ->
          do! processVideo replyToMessage resp queueConversion fileId fileName
        | None ->
          return ()
      }

  let private processMessageFromNewUser (repo: #ISaveUser) getLocaleTranslations queueUserConversion parseCommand replyToMessage =
    fun userId chatId userMessageId (message: Message) ->
      task {
        let user =
          { Id = userId
            Lang = message.From.LanguageCode |> Option.ofObj
            Banned = false }

        do! repo.SaveUser user

        let! translations = getLocaleTranslations user.Lang

        return!
          processIncomingMessage parseCommand translations (queueUserConversion userMessageId (Some userId) chatId) replyToMessage message
      }

  let private processMessageFromKnownUser getLocaleTranslations queueUserConversion parseCommand replyToMessage =
    fun (user: User) userMessageId chatId message ->
      task {
        let! translations = getLocaleTranslations user.Lang

        return!
          processIncomingMessage parseCommand translations (queueUserConversion userMessageId (Some user.Id) chatId) replyToMessage message
      }

  let processPrivateMessage
    (loadResources: Resources.LoadResources)
    (userRepo: #ILoadUser)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    (buildBotService: BuildExtendedBotService)
    : ProcessPrivateMessage =
    fun message ->
      let userId = message.From.Id |> UserId
      let chatId = message.Chat.Id |> ChatId
      let botService = buildBotService chatId
      let userMessageId = message.MessageId |> ChatMessageId
      let replyToMessage = Func.wrap2 botService.ReplyToMessage userMessageId

      let processMessageFromKnownUser =
        processMessageFromKnownUser loadResources queueUserConversion parseCommand replyToMessage

      let processMessageFromNewUser =
        processMessageFromNewUser userRepo loadResources queueUserConversion parseCommand replyToMessage

      Logf.logfi logger "Processing private message from user %i{UserId}" userId.Value

      task {
        let! user = userRepo.LoadUser userId

        match user with
        | Some u when u.Banned ->
            let! resp = loadResources u.Lang

            do! replyToMessage (resp[Resources.UserBan]) |> Task.ignore
        | Some u ->
          do! processMessageFromKnownUser u userMessageId chatId message
        | None ->
          do! processMessageFromNewUser userId chatId userMessageId message
      }

  let processGroupMessage
    (loadResources: Resources.LoadResources)
    (userRepo: #ILoadUser)
    (groupRepo: #ILoadGroup & #ISaveGroup)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    (buildBotService: BuildExtendedBotService)
    : ProcessGroupMessage =
    fun message ->
      let groupId = message.Chat.Id |> GroupId
      let userId = message.From.Id |> UserId
      let userMessageId = message.MessageId |> ChatMessageId
      let chatId = message.Chat.Id |> ChatId

      let botService = buildBotService chatId
      let replyToMessage = Func.wrap2 botService.ReplyToMessage userMessageId

      let processMessageFromKnownUser =
        processMessageFromKnownUser loadResources queueUserConversion parseCommand replyToMessage

      let processMessageFromNewUser =
        processMessageFromNewUser userRepo loadResources queueUserConversion parseCommand replyToMessage

      Logf.logfi logger "Processing message from user %i{UserId} in group %i{ChatId}" userId.Value groupId.Value

      task {
        let! user = userRepo.LoadUser userId
        let! group = groupRepo.LoadGroup groupId

        match user, group with
        | _, Some g when g.Banned ->
          let! resp = loadResources None
          do! replyToMessage (resp[Resources.GroupBan]) |> Task.ignore
        | Some u, _ when u.Banned ->
          let! resp = loadResources u.Lang

          do! replyToMessage (resp[Resources.UserBan]) |> Task.ignore
        | Some u, Some g -> processMessageFromKnownUser u userMessageId chatId message
        | Some u, None ->
          do! groupRepo.SaveGroup { Id = groupId; Banned = false }

          return! processMessageFromKnownUser u userMessageId chatId message
        | None, Some g -> processMessageFromNewUser userId chatId userMessageId message
        | _ ->
          do! groupRepo.SaveGroup { Id = groupId; Banned = false }

          return! processMessageFromNewUser userId chatId userMessageId message
      }

  let processChannelPost
    (createDefaultResourceProvider: CreateDefaultResourceProvider)
    (channelRepo: #ILoadChannel & #ISaveChannel)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    (buildBotService: BuildExtendedBotService)
    : ProcessChannelPost =
    fun post ->
      let channelId = post.Chat.Id |> ChannelId.Create
      let chatId = post.Chat.Id |> ChatId
      let postId = (post.MessageId |> ChatMessageId)
      let queueConversion = (queueUserConversion postId None chatId)

      let botService = buildBotService (post.Chat.Id |> ChatId)
      let replyToMessage = Func.wrap2 botService.ReplyToMessage postId

      Logf.logfi logger "Processing post from channel %i{ChannelId}" channelId.Value

      task {
        let! resp = createDefaultResourceProvider ()
        let! channel = channelRepo.LoadChannel channelId

        match channel with
        | Some c when c.Banned ->
          do! replyToMessage (resp[Resources.ChannelBan]) |> Task.ignore
        | Some _ ->
          do! processIncomingMessage parseCommand resp queueConversion replyToMessage post
        | None ->
          do! channelRepo.SaveChannel { Id = channelId; Banned = false }

          return! processIncomingMessage parseCommand resp queueConversion replyToMessage post
      }

type FFMpegBot
  (
    userConversionRepo: IUserConversionRepo,
    conversionRepo: IConversionRepo,
    conversionService: IConversionService,
    loadTranslations: User.LoadResources,
    buildBotService: BuildExtendedBotService,
    logger: ILogger<FFMpegBot>
  ) =
  interface IFFMpegBot with
    member this.PrepareConversion(conversionId, file) =
      task {
        let! userConversion = userConversionRepo.LoadUserConversion conversionId

        let! resp = userConversion.UserId |> loadTranslations

        let botService = buildBotService userConversion.ChatId
        let editMessage = Func.wrap2 botService.EditMessage userConversion.SentMessageId

        match! conversionService.PrepareConversion(conversionId, file) with
        | Ok _ -> do! editMessage resp[Resources.ConversionInProgress]
        | Error New.DownloadLinkError.Unauthorized -> do! editMessage resp[Resources.NotAuthorized]
        | Error New.DownloadLinkError.NotFound -> do! editMessage resp[Resources.NotFound]
        | Error New.DownloadLinkError.ServerError -> do! editMessage resp[Resources.ServerError]
      }

    member this.SaveVideo(conversionId, result) =
      task {
        let! userConversion = userConversionRepo.LoadUserConversion conversionId

        let botService = buildBotService userConversion.ChatId
        let editMessage = Func.wrap2 botService.EditMessage userConversion.SentMessageId

        let! resp = userConversion.UserId |> loadTranslations

        let! conversion = conversionRepo.LoadConversion conversionId

        match result with
        | ConversionResult.Success file ->
          let video = Conversion.Video file

          match conversion with
          | Prepared preparedConversion ->
            do! conversionService.SaveVideo(preparedConversion, video) |> Task.ignore
            do! editMessage resp[Resources.VideoConverted]
          | Thumbnailed thumbnailedConversion ->
            let! completed = conversionService.CompleteConversion(thumbnailedConversion, video)
            do! conversionRepo.QueueUpload completed
            do! editMessage resp[Resources.Uploading]
          | _ ->
            logger.LogError("Conversion {ConversionId} is not thumbnailed to be completed!", conversionId.Value)

            do! editMessage resp[Resources.ConversionError]
        | ConversionResult.Error _ -> do! editMessage resp[Resources.ConversionError]
      }

    member this.SaveThumbnail(conversionId, result) =
      task {
        let! userConversion = userConversionRepo.LoadUserConversion conversionId

        let botService = buildBotService userConversion.ChatId
        let editMessage = Func.wrap2 botService.EditMessage userConversion.SentMessageId

        let! resp = userConversion.UserId |> loadTranslations

        let! conversion = conversionRepo.LoadConversion conversionId

        match result with
        | ConversionResult.Success file ->
          let video = Thumbnail file

          match conversion with
          | Prepared preparedConversion ->
            do! conversionService.SaveThumbnail(preparedConversion, video) |> Task.ignore
            do! editMessage resp[Resources.ThumbnailGenerated]
          | Converted convertedConversion ->
            let! completed = conversionService.CompleteConversion(convertedConversion, video)
            do! conversionRepo.QueueUpload completed
            do! editMessage resp[Resources.Uploading]
          | _ ->
            logger.LogError("Conversion {ConversionId} is not converted to be completed!", conversionId.Value)

            do! editMessage resp[Resources.ConversionError]
        | ConversionResult.Error _ -> do! editMessage resp[Resources.ThumbnailingError]
      }

    member this.UploadConversion(id) =
      task {
        let! userConversion = userConversionRepo.LoadUserConversion id

        let botService = buildBotService userConversion.ChatId

        let! conversion = conversionRepo.LoadConversion id
        let! resp = userConversion.UserId |> loadTranslations

        match conversion with
        | Completed conversion ->

          do!
            botService.ReplyWithVideo(
              userConversion.ReceivedMessageId,
              resp[Resources.Completed],
              conversion.OutputFile,
              conversion.ThumbnailFile
            )

          do! conversionService.CleanupConversion conversion
          do! botService.DeleteBotMessage userConversion.SentMessageId
        | _ ->
          logger.LogError("Conversion {ConversionId} is not completed to be uploaded!", id.Value)

          do! botService.EditMessage(userConversion.SentMessageId, resp[Resources.ConversionError])
      }