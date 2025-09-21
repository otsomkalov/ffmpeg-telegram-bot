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
    parseCommand: ParseCommand,
    chatSvc: IChatSvc,
    createConversion: Create,
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

  let processLinks replyToMessage (resp: IResourceProvider) queueUserConversion links =
    let sendUrlToQueue (url: string) =
      task {
        let! sentMessageId = replyToMessage (resp[Resources.LinkDownload, [| url |]])

        do! queueUserConversion sentMessageId (Conversion.New.InputFile.Link { Url = url })
      }

    links |> Seq.map sendUrlToQueue |> Task.WhenAll |> Task.ignore

  let processDocument replyToMessage (resp: IResourceProvider) queueUserConversion fileId fileName =
    task {
      let! sentMessageId = replyToMessage (resp[Resources.DocumentDownload, [| fileName |]])

      do! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = fileId; Name = fileName })
    }

  let processVideo replyToMessage (resp: IResourceProvider) queueUserConversion fileId fileName =
    task {
      let! sentMessageId = replyToMessage (resp[Resources.VideoDownload, [| fileName |]])

      do! queueUserConversion sentMessageId (Conversion.New.InputFile.Document { Id = fileId; Name = fileName })
    }

  let processMessage replyToMessage queueConversion (resp: IResourceProvider) =
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

  interface IFFMpegBot with
    member this.ProcessMessage(message: Message) =
      let chatId = message.Chat.Id |> ChatId
      let messageId = message.MessageId |> ChatMessageId
      let queueConversion = queueProcessing messageId chatId

      let botService = buildBotService (message.Chat.Id |> ChatId)
      let replyToMessage = Func.wrap2 botService.ReplyToMessage messageId

      task {
        let! chat = chatRepo.LoadChat chatId

        match chat with
        | Some c ->
          let! resp = loadResources c.Lang

          if c.Banned then
            do! replyToMessage (resp[Resources.ChannelBan]) |> Task.ignore
          else
            do! processMessage replyToMessage queueConversion resp message
        | None ->
          let lang =
            message.From
            |> Option.ofObj
            |> Option.bind (fun u -> u.LanguageCode |> Option.ofObj)

          let! chat = chatSvc.CreateChat(chatId, lang)
          let! resp = loadResources chat.Lang

          do! processMessage replyToMessage queueConversion resp message
      }

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