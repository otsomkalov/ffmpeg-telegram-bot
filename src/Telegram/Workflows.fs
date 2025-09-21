namespace Telegram

open Telegram.Bot.Types
open Telegram
open System.Threading.Tasks
open Domain
open Domain.Core
open Domain.Core.Conversion
open FSharp
open Microsoft.Extensions.Logging
open Microsoft.FSharp.Core
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
      fun userMessageId chatId sentMessageId inputFile ->
        task {
          let! conversion = createConversion ()

          do!
            repo.SaveUserConversion
              { ChatId = chatId
                SentMessageId = sentMessageId
                ReceivedMessageId = userMessageId
                ConversionId = conversion.Id }

          do! conversionRepo.QueuePreparation(conversion.Id, inputFile)
        }

  [<RequireQualifiedAccess>]
  module Chat =
    let private loadResources' (loadResources: CreateResourceProvider) (loadDefaultResources: CreateDefaultResourceProvider) =
      function
      | Some l -> loadResources l
      | None -> loadDefaultResources ()

    let loadResources
      (repo: #ILoadChat)
      (createResourceProvider: CreateResourceProvider)
      (loadDefaultResources: CreateDefaultResourceProvider)
      : Chat.LoadResources =
      let loadResources = loadResources' createResourceProvider loadDefaultResources

      repo.LoadChat >> Task.bind (Option.map _.Lang >> loadResources)

  let private processLinks replyToMessage (resp: IResourceProvider) queueUserConversion links =
    let sendUrlToQueue (url: string) =
      task {
        let! sentMessageId = replyToMessage (resp[Resources.LinkDownload, [| url |]])

        do! queueUserConversion sentMessageId (Conversion.New.InputFile.Link { Url = url })
      }

    links |> Seq.map sendUrlToQueue |> Task.WhenAll |> Task.ignore

  let private processDocument replyToMessage (resp: IResourceProvider) queueUserConversion fileId fileName =
    task {
      let! sentMessageId = replyToMessage (resp[Resources.DocumentDownload, [| fileName |]])

      do! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = fileId; Name = fileName })
    }

  let private processVideo replyToMessage (resp: IResourceProvider) queueUserConversion fileId fileName =
    task {
      let! sentMessageId = replyToMessage (resp[Resources.VideoDownload, [| fileName |]])

      do! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = fileId; Name = fileName })
    }

  let private processIncomingMessage parseCommand (resp: IResourceProvider) queueConversion replyToMessage =
    fun message ->
      task {
        let! command = parseCommand message

        match command with
        | Some(Command.Start) -> do! replyToMessage (resp[Resources.Welcome]) |> Task.ignore
        | Some(Command.Links links) -> do! processLinks replyToMessage resp queueConversion links
        | Some(Command.Document(fileId, fileName)) -> do! processDocument replyToMessage resp queueConversion fileId fileName
        | Some(Command.Video(fileId, fileName)) -> do! processVideo replyToMessage resp queueConversion fileId fileName
        | None -> return ()
      }

  let private processMessageFromNewUser (chatSvc: #ICreateChat) getLocaleTranslations queueUserConversion parseCommand replyToMessage =
    fun chatId userMessageId (message: Message) ->
      task {
        let! chat = chatSvc.CreateChat(chatId, message.From.LanguageCode |> Option.ofObj)

        let! translations = getLocaleTranslations chat.Lang

        do! processIncomingMessage parseCommand translations (queueUserConversion userMessageId chatId) replyToMessage message
      }

  let private processMessageFromKnownUser getLocaleTranslations queueUserConversion parseCommand replyToMessage =
    fun (chat: Chat) userMessageId message ->
      task {
        let! translations = getLocaleTranslations chat.Lang

        do! processIncomingMessage parseCommand translations (queueUserConversion userMessageId chat.Id) replyToMessage message
      }

  let processPrivateMessage
    (chatRepo: IChatRepo)
    (chatSvc: IChatSvc)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    (createResourceProvider: CreateResourceProvider)
    (buildBotService: BuildExtendedBotService)
    : ProcessPrivateMessage =
    fun message ->
      let chatId = message.Chat.Id |> ChatId
      let botService = buildBotService chatId
      let userMessageId = message.MessageId |> ChatMessageId
      let replyToMessage = Func.wrap2 botService.ReplyToMessage userMessageId

      let processMessageFromKnownUser =
        processMessageFromKnownUser createResourceProvider queueUserConversion parseCommand replyToMessage

      let processMessageFromNewUser =
        processMessageFromNewUser chatSvc createResourceProvider queueUserConversion parseCommand replyToMessage

      Logf.logfi logger "Processing private message from user %i{UserId}" chatId.Value

      task {
        let! chat = chatRepo.LoadChat chatId

        match chat with
        | Some u ->
          let! resp = createResourceProvider u.Lang

          if u.Banned then
            do! replyToMessage (resp[Resources.UserBan]) |> Task.ignore
          else
            do! processMessageFromKnownUser u userMessageId message
        | None -> do! processMessageFromNewUser chatId userMessageId message
      }

  let processGroupMessage
    (chatRepo: #ILoadChat)
    (chatSvc: #ICreateChat)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    (createResourceProvider: CreateResourceProvider)
    (buildBotService: BuildExtendedBotService)
    : ProcessGroupMessage =
    fun message ->
      let userId = message.From.Id |> ChatId
      let groupId = message.Chat.Id |> ChatId
      let userMessageId = message.MessageId |> ChatMessageId

      let botService = buildBotService groupId
      let replyToMessage = Func.wrap2 botService.ReplyToMessage userMessageId

      let processMessageFromKnownUser =
        processMessageFromKnownUser createResourceProvider queueUserConversion parseCommand replyToMessage

      let processMessageFromNewUser =
        processMessageFromNewUser chatSvc createResourceProvider queueUserConversion parseCommand replyToMessage

      Logf.logfi logger "Processing message from user %i{UserId} in group %i{ChatId}" userId.Value groupId.Value

      task {
        let! group = chatRepo.LoadChat groupId

        let! user = chatRepo.LoadChat userId

        match user, group with
        | _, Some g when g.Banned ->
          let! resp = createResourceProvider g.Lang

          do! replyToMessage (resp[Resources.GroupBan]) |> Task.ignore
        | Some u, _ when u.Banned ->
          let! resp = createResourceProvider u.Lang

          do! replyToMessage (resp[Resources.UserBan]) |> Task.ignore
        | Some u, Some g -> do! processMessageFromKnownUser g userMessageId message
        | Some u, None ->
          let! grp = chatSvc.CreateChat(groupId, None)

          do! processMessageFromKnownUser grp userMessageId message
        | None, Some g -> do! processMessageFromNewUser groupId userMessageId message
        | _ ->
          let! grp = chatSvc.CreateChat(groupId, None)

          do! processMessageFromNewUser groupId userMessageId message
      }

  let processChannelPost
    (chatRepo: IChatRepo)
    (chatSvc: IChatSvc)
    (queueUserConversion: UserConversion.QueueProcessing)
    (parseCommand: ParseCommand)
    (logger: ILogger)
    (createResourceProvider: CreateResourceProvider)
    (buildBotService: BuildExtendedBotService)
    : ProcessChannelPost =
    fun post ->
      let chatId = post.Chat.Id |> ChatId
      let postId = (post.MessageId |> ChatMessageId)
      let queueConversion = (queueUserConversion postId chatId)

      let botService = buildBotService (post.Chat.Id |> ChatId)
      let replyToMessage = Func.wrap2 botService.ReplyToMessage postId

      Logf.logfi logger "Processing post from channel %i{ChannelId}" chatId.Value

      task {
        let! channel = chatRepo.LoadChat chatId

        match channel with
        | Some c ->
          let! resp = createResourceProvider c.Lang

          if c.Banned then
            do! replyToMessage (resp[Resources.ChannelBan]) |> Task.ignore
          else
            do! processIncomingMessage parseCommand resp queueConversion replyToMessage post
        | None ->
          let! chat = chatSvc.CreateChat(chatId, None)
          let! resp = createResourceProvider chat.Lang

          do! processIncomingMessage parseCommand resp queueConversion replyToMessage post
      }

type ChatSvc(resourcesSettings: ResourcesSettings, chatRepo: IChatRepo) =
  interface IChatSvc with
    member this.CreateChat(chatId, lang) =
      let chat: Chat =
        { Id = chatId
          Lang = lang |> Option.defaultValue resourcesSettings.DefaultLang
          Banned = false }

      task {
        do! chatRepo.SaveChat chat

        return chat
      }

type FFMpegBot
  (
    userConversionRepo: IUserConversionRepo,
    conversionRepo: IConversionRepo,
    conversionService: IConversionService,
    loadTranslations: Chat.LoadResources,
    buildBotService: BuildExtendedBotService,
    logger: ILogger<FFMpegBot>
  ) =
  interface IFFMpegBot with
    member this.PrepareConversion(conversionId, file) =
      task {
        let! userConversion = userConversionRepo.LoadUserConversion conversionId

        let! resp = userConversion.ChatId |> loadTranslations

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

        let! resp = userConversion.ChatId |> loadTranslations

        match result with
        | ConversionResult.Success file ->
          let video = Video file

          match! conversionRepo.LoadConversion conversionId with
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

        let! resp = userConversion.ChatId |> loadTranslations

        match result with
        | ConversionResult.Success file ->
          let video = Thumbnail file

          match! conversionRepo.LoadConversion conversionId with
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

        let! resp = userConversion.ChatId |> loadTranslations

        match! conversionRepo.LoadConversion id with
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