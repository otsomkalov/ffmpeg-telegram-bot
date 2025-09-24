namespace Telegram

open Telegram
open System.Threading.Tasks
open Domain
open Domain.Core
open Domain.Core.Conversion
open Microsoft.Extensions.Logging
open Microsoft.FSharp.Core
open Telegram.Core
open Telegram.Handlers
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
    handlerFactories: MsgHandlerFactory seq,
    logger: ILogger<FFMpegBot>
  ) =

  let loadResources' =
    chatRepo.LoadChat
    >> Task.bind (
      Option.map _.Lang
      >> (function
      | Some l -> loadResources l
      | None -> loadDefaultResources ())
    )

  let globalHandler bot resp =
    fun msg ->
      task {
        let handlers = handlerFactories |> Seq.map (fun f -> f bot resp)

        let mutable lastHandlerResult = None
        let mutable e = handlers.GetEnumerator()

        while e.MoveNext() && lastHandlerResult.IsNone do
          let! handlerResult = e.Current msg

          lastHandlerResult <- handlerResult

        return ()
      }

  interface IFFMpegBot with
    member this.ProcessUpdate(update: Update) =
      match update with
      | Msg msg ->
        let botService = buildBotService msg.ChatId

        task {
          let! chat = chatRepo.LoadChat msg.ChatId

          match chat with
          | Some c ->
            let! resp = loadResources c.Lang

            if c.Banned then
              do!
                botService.ReplyToMessage(msg.MessageId, resp[Resources.ChannelBan])
                |> Task.ignore
            else
              do! globalHandler botService resp msg
          | None ->
            let! chat = chatSvc.CreateChat(msg.ChatId, msg.Lang)
            let! resp = loadResources chat.Lang

            do! globalHandler botService resp msg
        }
      | Other type' ->
        logger.LogInformation("Got unsupported update type {Type}!", type'.ToString())
        Task.FromResult()

    member this.PrepareConversion(conversionId, file) =
      task {
        let! userConversion = userConversionRepo.LoadUserConversion conversionId

        let! resp = userConversion.ChatId |> loadResources'

        let botService = buildBotService userConversion.ChatId
        match! conversionService.PrepareConversion(conversionId, file) with
        | Ok _ -> do! botService.EditMessage(userConversion.SentMessageId, resp[Resources.ConversionInProgress])
        | Error New.DownloadLinkError.Unauthorized -> do! botService.EditMessage(userConversion.SentMessageId, resp[Resources.NotAuthorized])
        | Error New.DownloadLinkError.NotFound -> do! botService.EditMessage(userConversion.SentMessageId, resp[Resources.NotFound])
        | Error New.DownloadLinkError.ServerError -> do! botService.EditMessage(userConversion.SentMessageId, resp[Resources.ServerError])
      }

    member this.SaveVideo(conversionId, result) =
      task {
        let! userConversion = userConversionRepo.LoadUserConversion conversionId

        let botService = buildBotService userConversion.ChatId
        let! resp = userConversion.ChatId |> loadResources'

        match result with
        | ConversionResult.Success file ->
          let video = Video file

          match! conversionRepo.LoadConversion conversionId with
          | Prepared preparedConversion ->
            do! conversionService.SaveVideo(preparedConversion, video) |> Task.ignore
            do! botService.EditMessage(userConversion.SentMessageId, resp[Resources.VideoConverted])
          | Thumbnailed thumbnailedConversion ->
            let! completed = conversionService.CompleteConversion(thumbnailedConversion, video)
            do! conversionRepo.QueueUpload completed
            do! botService.EditMessage(userConversion.SentMessageId, resp[Resources.Uploading])
          | _ ->
            logger.LogError("Conversion {ConversionId} is not thumbnailed to be completed!", conversionId.Value)

            do! botService.EditMessage(userConversion.SentMessageId, resp[Resources.ConversionError])
        | ConversionResult.Error _ -> do! botService.EditMessage(userConversion.SentMessageId, resp[Resources.ConversionError])
      }

    member this.SaveThumbnail(conversionId, result) =
      task {
        let! userConversion = userConversionRepo.LoadUserConversion conversionId

        let botService = buildBotService userConversion.ChatId
        let! resp = userConversion.ChatId |> loadResources'

        match result with
        | ConversionResult.Success file ->
          let video = Thumbnail file

          match! conversionRepo.LoadConversion conversionId with
          | Prepared preparedConversion ->
            do! conversionService.SaveThumbnail(preparedConversion, video) |> Task.ignore
            do! botService.EditMessage(userConversion.SentMessageId, resp[Resources.ThumbnailGenerated])
          | Converted convertedConversion ->
            let! completed = conversionService.CompleteConversion(convertedConversion, video)
            do! conversionRepo.QueueUpload completed
            do! botService.EditMessage(userConversion.SentMessageId, resp[Resources.Uploading])
          | _ ->
            logger.LogError("Conversion {ConversionId} is not converted to be completed!", conversionId.Value)

            do! botService.EditMessage(userConversion.SentMessageId, resp[Resources.ConversionError])
        | ConversionResult.Error _ -> do! botService.EditMessage(userConversion.SentMessageId, resp[Resources.ThumbnailingError])
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