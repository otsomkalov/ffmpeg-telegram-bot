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
            replyToMessageId = messageId.Value,
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
module Translation =
  let private loadTranslationsMap (collection: IMongoCollection<Entities.Translation>) key =
    collection.Find(fun t -> t.Lang = key).ToListAsync()
    |> Task.map (
      Seq.groupBy _.Key
      >> Seq.map (fun (key, translations) -> (key, translations |> Seq.map (_.Value) |> Seq.head))
      >> Map.ofSeq
    )

  let private formatWithFallback formats fallback =
    fun (key: string, args: obj seq) ->
      match formats |> Map.tryFind key with
      | Some fmt -> String.Format(fmt, args |> Array.ofSeq)
      | None -> fallback

  let loadDefaultTranslations
    (collection: IMongoCollection<Entities.Translation>)
    (loggerFactory: ILoggerFactory)
    : Translation.LoadDefaultTranslations =
    let logger = loggerFactory.CreateLogger(nameof Translation.LoadDefaultTranslations)

    fun () ->
      task {
        Logf.logfi logger "Loading default translations"
        let! translations = loadTranslationsMap collection Translation.DefaultLang
        Logf.logfi logger "Default translations map loaded from DB"

        let getTranslation =
          fun key -> translations |> Map.tryFind key |> Option.defaultValue key

        let formatTranslation =
          fun (key, args) -> formatWithFallback translations key (key, args)

        return (getTranslation, formatTranslation)
      }

  let loadTranslations
    (collection: IMongoCollection<Entities.Translation>)
    (loggerFactory: ILoggerFactory)
    (loadDefaultTranslations: Translation.LoadDefaultTranslations)
    : Translation.LoadTranslations =
    let logger = loggerFactory.CreateLogger(nameof Translation.LoadTranslations)

    function
    | Some l when l <> Translation.DefaultLang ->
      task {
        let! tran, tranf = loadDefaultTranslations ()

        Logf.logfi logger "Loading translations for lang %s{Lang}" l

        let! localeTranslations = loadTranslationsMap collection l

        Logf.logfi logger "Translations for lang %s{Lang} is loaded" l

        let getTranslation: Translation.GetTranslation =
          fun key -> localeTranslations |> Map.tryFind key |> Option.defaultValue (tran key)

        let formatTranslation: Translation.FormatTranslation =
          fun (key, args) -> formatWithFallback localeTranslations (tranf (key, args)) (key, args)

        return (getTranslation, formatTranslation)
      }
    | _ -> loadDefaultTranslations ()