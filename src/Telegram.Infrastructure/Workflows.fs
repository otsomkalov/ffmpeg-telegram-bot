﻿namespace Telegram.Infrastructure

open System.Text.RegularExpressions
open Azure.Storage.Blobs
open Domain.Core
open FSharp
open Infrastructure.Helpers
open Infrastructure.Settings
open Microsoft.Extensions.Logging
open MongoDB.Driver
open Telegram.Bot
open Telegram.Core
open Telegram.Bot.Types
open Telegram.Infrastructure.Settings
open Telegram.Workflows
open Telegram.Infrastructure.Core
open otsom.fs.Extensions
open otsom.fs.Telegram.Bot.Core
open System.Threading.Tasks
open System
open Domain.Repos
open Telegram.Infrastructure.Helpers
open otsom.fs.Extensions.String

module Workflows =
  let deleteBotMessage (bot: ITelegramBotClient) : DeleteBotMessage =
    fun userId messageId -> bot.DeleteMessageAsync((userId |> UserId.value |> ChatId), (messageId |> BotMessageId.value))

  let replyWithVideo (workersSettings: WorkersSettings) (bot: ITelegramBotClient) : ReplyWithVideo =
    let blobServiceClient = BlobServiceClient(workersSettings.ConnectionString)

    fun userId messageId ->
      fun text video thumbnail ->
        let (Conversion.Video video) = video
        let (Conversion.Thumbnail thumbnail) = thumbnail

        let videoContainer =
          blobServiceClient.GetBlobContainerClient workersSettings.Converter.Output.Container

        let thumbnailContainer =
          blobServiceClient.GetBlobContainerClient workersSettings.Thumbnailer.Output.Container

        let videoBlob = videoContainer.GetBlobClient(video)
        let thumbnailBlob = thumbnailContainer.GetBlobClient(thumbnail)

        [ videoBlob.DownloadStreamingAsync(); thumbnailBlob.DownloadStreamingAsync() ]
        |> Task.WhenAll
        |> Task.bind (fun [| videoStreamResponse; thumbnailStreamResponse |] ->
          bot.SendVideoAsync(
            (userId |> UserId.value |> ChatId),
            InputFileStream(videoStreamResponse.Value.Content, video),
            caption = text,
            replyToMessageId = (messageId |> UserMessageId.value),
            thumbnail = InputFileStream(thumbnailStreamResponse.Value.Content, thumbnail),
            disableNotification = true
          ))
        |> Task.ignore

  let parseCommand (settings: InputValidationSettings) : ParseCommand =
    let linkRegex = Regex(settings.LinkRegex)

    fun message ->
      match message with
      | FromBot -> None |> Task.FromResult
      | Text messageText ->
        match messageText with
        | StartsWith "/start" -> Command.Start |> Some |> Task.FromResult
        | Regex linkRegex matches -> matches |> Command.Links |> Some |> Task.FromResult
        | _ -> None |> Task.FromResult
      | Document settings.MimeTypes doc -> Command.Document(doc.FileId, doc.FileName) |> Some |> Task.FromResult
      | Video settings.MimeTypes vid -> Command.Video(vid.FileId, vid.FileName) |> Some |> Task.FromResult
      | _ -> None |> Task.FromResult

[<RequireQualifiedAccess>]
module Conversion =

  [<RequireQualifiedAccess>]
  module New =
    [<RequireQualifiedAccess>]
    module InputFile =
      let downloadDocument (bot: ITelegramBotClient) (workersSettings: WorkersSettings) : Conversion.New.InputFile.DownloadDocument =
        fun document ->
          task {
            use! converterBlobStream = Storage.getBlobStream workersSettings document.Name workersSettings.Converter.Input.Container

            do! bot.GetInfoAndDownloadFileAsync(document.Id, converterBlobStream) |> Task.ignore

            use! thumbnailerBlobStream = Storage.getBlobStream workersSettings document.Name workersSettings.Thumbnailer.Input.Container

            do!
              bot.GetInfoAndDownloadFileAsync(document.Id, thumbnailerBlobStream)
              |> Task.ignore

            return document.Name
          }

[<RequireQualifiedAccess>]
module Translation =
  let private loadTranslationsMap (collection: IMongoCollection<Database.Translation>) logger =
    fun lang ->
      task {
        Logf.logfi logger "Loading translations for lang %s{Lang}" lang

        let! trans = collection.Find(fun t -> t.Lang = lang).ToListAsync()

        Logf.logfi logger "Translations for lang %s{Lang} is loaded" lang

        return
          trans
          |> Seq.groupBy (_.Key)
          |> Seq.map (fun (key, translations) -> (key, translations |> Seq.map (_.Value) |> Seq.head))
          |> Map.ofSeq
      }

  let private formatWithFallback formats fallback =
    fun (key, args) ->
      match formats |> Map.tryFind key with
      | Some fmt -> String.Format(fmt, args)
      | None -> fallback

  let loadDefaultTranslations (collection: IMongoCollection<Database.Translation>) (loggerFactory: ILoggerFactory) : Translation.LoadDefaultTranslations =
    let logger = loggerFactory.CreateLogger(nameof Translation.LoadDefaultTranslations)

    fun () ->
      task {
        let! translations = loadTranslationsMap collection logger Translation.DefaultLang

        let getTranslation =
          fun key -> translations |> Map.tryFind key |> Option.defaultValue key

        let formatTranslation =
          fun (key, args) -> formatWithFallback translations key (key, args)

        return (getTranslation, formatTranslation)
      }

  let loadTranslations (collection: IMongoCollection<Database.Translation>) (loggerFactory: ILoggerFactory) (loadDefaultTranslations: Translation.LoadDefaultTranslations) : Translation.LoadTranslations =
    let logger = loggerFactory.CreateLogger(nameof Translation.LoadTranslations)

    function
    | Some lang when lang <> Translation.DefaultLang ->
      task {
        let! tran, tranf = loadDefaultTranslations ()

        let! localeTranslations = loadTranslationsMap collection logger lang

        let getTranslation: Translation.GetTranslation =
          fun key -> localeTranslations |> Map.tryFind key |> Option.defaultValue (tran key)

        let formatTranslation: Translation.FormatTranslation =
          fun (key, args) -> formatWithFallback localeTranslations (tranf (key, args)) (key, args)

        return (getTranslation, formatTranslation)
      }
    | _ -> loadDefaultTranslations ()
