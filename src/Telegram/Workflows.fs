namespace Telegram

open Telegram.Bot.Types
open Telegram
open System.Threading.Tasks
open Domain
open Domain.Core
open Domain.Core.Conversion
open Microsoft.Extensions.Logging
open Microsoft.FSharp.Core
open Telegram.Core
open Telegram.Handlers
open Telegram.Helpers
open otsom.fs.Bot
open otsom.fs.Resources
open otsom.fs.Extensions
open Telegram.Repos

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
    loadResources: CreateResourceProvider,
    loadDefaultResources: CreateDefaultResourceProvider,
    buildBotService: BuildExtendedBotService,
    chatRepo: IChatRepo,
    chatSvc: IChatSvc,
    createConversion: Create,
    handlerFactories: MsgHandlerFactory list,
    logger: ILogger<FFMpegBot>
  ) =

  let queueProcessing =
    fun userMessageId chatId sentMessageId inputFile ->
      task {
        let! conversion = createConversion ()

        do!
          userConversionRepo.SaveUserConversion
            { ChatId = chatId
              SentMessageId = sentMessageId
              ReceivedMessageId = userMessageId
              ConversionId = conversion.Id }

        do! conversionRepo.QueuePreparation(conversion.Id, inputFile)
      }

  let loadResources' =
    chatRepo.LoadChat
    >> Task.bind (
      Option.map _.Lang
      >> (function
      | Some l -> loadResources l
      | None -> loadDefaultResources ())
    )

  let processMessage =
    fun (message: Message) ->
      let chatId = message.Chat.Id |> ChatId
      let messageId = message.MessageId |> ChatMessageId
      let queueConversion = queueProcessing messageId chatId

      let botService = buildBotService (message.Chat.Id |> ChatId)
      let replyToMessage = Func.wrap2 botService.ReplyToMessage messageId

      let msg: Msg =
        { MessageId = messageId
          Text = message.Text |> Option.ofObj
          Doc =
            message.Document
            |> Option.ofObj
            |> Option.map (fun doc ->
              { Id = doc.FileId
                Name = doc.FileName
                MimeType = doc.MimeType
                Caption = message.Caption |> Option.ofObj })
          Vid =
            message.Video
            |> Option.ofObj
            |> Option.map (fun vid ->
              { Id = vid.FileId
                Name = vid.FileName |> Option.ofObj
                MimeType = vid.MimeType
                Caption = message.Caption |> Option.ofObj }) }

      task {
        let! chat = chatRepo.LoadChat chatId

        match chat with
        | Some c ->
          let! resp = loadResources c.Lang

          let handler = globalHandler botService resp handlerFactories

          if c.Banned then
            do! replyToMessage (resp[Resources.ChannelBan]) |> Task.ignore
          else
            do! handler msg
        | None ->
          let lang =
            message.From
            |> Option.ofObj
            |> Option.bind (fun u -> u.LanguageCode |> Option.ofObj)

          let! chat = chatSvc.CreateChat(chatId, lang)
          let! resp = loadResources chat.Lang

          let handler = globalHandler botService resp handlerFactories

          do! handler msg
      }

  interface IFFMpegBot with
    member this.ProcessUpdate(update: Update) =
      match update with
      | Msg msg -> processMessage msg
      | Other type' ->
        logger.LogInformation("Got unsupported update type {Type}!", type'.ToString())
        Task.FromResult()

    member this.PrepareConversion(conversionId, file) =
      task {
        let! userConversion = userConversionRepo.LoadUserConversion conversionId

        let! resp = userConversion.ChatId |> loadResources'

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

        let! resp = userConversion.ChatId |> loadResources'

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

        let! resp = userConversion.ChatId |> loadResources'

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

        let! resp = userConversion.ChatId |> loadResources'

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