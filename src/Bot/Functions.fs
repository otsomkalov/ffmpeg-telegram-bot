namespace Bot.Functions

open System.Threading.Tasks
open Domain.Workflows
open FSharp
open Infrastructure.Core
open Infrastructure.Queue
open Infrastructure.Settings
open Microsoft.AspNetCore.Http
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums
open Telegram.Core
open Telegram.Workflows
open otsom.fs.Telegram.Bot.Core
open Domain.Repos
open Domain.Core
open Telegram.Repos

type ConverterResultMessage =
  { Id: string; Result: ConversionResult }

type Functions
  (
    workersSettings: WorkersSettings,
    _logger: ILogger<Functions>,
    sendUserMessage: SendUserMessage,
    replyToUserMessage: ReplyToUserMessage,
    editBotMessage: EditBotMessage,
    loadUserConversion: UserConversion.Load,
    loadCompletedConversion: Conversion.Completed.Load,
    deleteBotMessage: DeleteBotMessage,
    replyWithVideo: ReplyWithVideo,
    deleteVideo: Conversion.Completed.DeleteVideo,
    deleteThumbnail: Conversion.Completed.DeleteThumbnail,
    getLocaleTranslations: Translation.GetLocaleTranslations,
    queueUpload: Conversion.Completed.QueueUpload,
    loadUser: User.Load,
    completeThumbnailedConversion: Conversion.Thumbnailed.Complete,
    completeConvertedConversion: Conversion.Converted.Complete,
    loadPreparedOrThumbnailed: Conversion.PreparedOrThumbnailed.Load,
    loadPreparedOrConverted: Conversion.PreparedOrConverted.Load,
    saveVideo: Conversion.Prepared.SaveVideo,
    saveThumbnail: Conversion.Prepared.SaveThumbnail,
    downloadLink: Conversion.New.InputFile.DownloadLink,
    downloadDocument: Conversion.New.InputFile.DownloadDocument,
    savePreparedConversion: Conversion.Prepared.Save,
    saveUserConversion: UserConversion.Save,
    ensureUserExists: User.EnsureExists,
    queueConversionPreparation: Conversion.New.QueuePreparation,
    parseCommand: ParseCommand,
    createConversion: Conversion.Create
  ) =

  [<Function("HandleUpdate")>]
  member this.HandleUpdate([<HttpTrigger("POST", Route = "telegram")>] request: HttpRequest, [<FromBody>] update: Update) : Task<unit> =

    let queueUserConversion =
      UserConversion.queueProcessing createConversion saveUserConversion queueConversionPreparation

    let processMessage =
      processMessage sendUserMessage replyToUserMessage getLocaleTranslations ensureUserExists queueUserConversion parseCommand

    task {
      try
        do!
          (match update.Type with
           | UpdateType.Message -> processMessage update.Message
           | UpdateType.ChannelPost -> processMessage update.ChannelPost
           | _ -> Task.FromResult())

        return ()
      with e ->
        Logf.elogfe _logger e "Error during processing an update:"
        return ()
    }

  [<Function("Downloader")>]
  member this.DownloadFile
    (
      [<QueueTrigger("%Workers:Downloader:Queue%", Connection = "Workers:ConnectionString")>] message: DownloaderMessage,
      _: FunctionContext
    ) : Task<unit> =
    let queueConversion = Conversion.Prepared.queueConversion workersSettings
    let queueThumbnailing = Conversion.Prepared.queueThumbnailing workersSettings

    let prepareConversion =
      Conversion.New.prepare downloadLink downloadDocument savePreparedConversion queueConversion queueThumbnailing

    let downloadFileAndQueueConversion =
      downloadFileAndQueueConversion editBotMessage loadUserConversion loadUser getLocaleTranslations prepareConversion

    downloadFileAndQueueConversion message.ConversionId message.File

  [<Function("SaveConversionResult")>]
  member this.SaveConversionResult
    (
      [<QueueTrigger("%Workers:Converter:Output:Queue%", Connection = "Workers:ConnectionString")>] message: ConverterResultMessage,
      _: FunctionContext
    ) : Task<unit> =
    let processConversionResult =
      processConversionResult
        loadUserConversion
        editBotMessage
        loadPreparedOrThumbnailed
        loadUser
        getLocaleTranslations
        saveVideo
        completeThumbnailedConversion
        queueUpload

    processConversionResult (ConversionId message.Id) message.Result

  [<Function("SaveThumbnailingResult")>]
  member this.SaveThumbnailingResult
    (
      [<QueueTrigger("%Workers:Thumbnailer:Output:Queue%", Connection = "Workers:ConnectionString")>] message: ConverterResultMessage,
      _: FunctionContext
    ) : Task<unit> =
    let processThumbnailingResult =
      processThumbnailingResult
        loadUserConversion
        editBotMessage
        loadPreparedOrConverted
        loadUser
        getLocaleTranslations
        saveThumbnail
        completeConvertedConversion
        queueUpload

    processThumbnailingResult (ConversionId message.Id) message.Result

  [<Function("Uploader")>]
  member this.Upload
    (
      [<QueueTrigger("%Workers:Uploader:Queue%", Connection = "Workers:ConnectionString")>] message: UploaderMessage,
      _: FunctionContext
    ) : Task =
    let conversionId = message.ConversionId |> ConversionId

    let uploadSuccessfulConversion =
      uploadCompletedConversion loadUserConversion loadCompletedConversion deleteBotMessage replyWithVideo deleteVideo deleteThumbnail

    uploadSuccessfulConversion conversionId
