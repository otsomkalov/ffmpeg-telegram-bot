﻿#nowarn "20"

namespace Telegram.Infrastructure

open System.Net.Http
open Domain.Repos
open Infrastructure.Settings
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open MongoDB.Driver
open Telegram.Bot
open Telegram.Core
open Telegram.Infrastructure.Settings
open Telegram.Infrastructure.Workflows
open Telegram.Workflows
open otsom.fs.Extensions.DependencyInjection
open Telegram.Repos
open Telegram.Infrastructure.Repos

module Startup =
  let addTelegram (services: IServiceCollection) =
    services
      .BuildSingleton<InputValidationSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(InputValidationSettings.SectionName)
          .Get<InputValidationSettings>())
      .BuildSingleton<TelegramSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(TelegramSettings.SectionName)
          .Get<TelegramSettings>())

    services
      .AddSingleton<HttpClientHandler>(fun _ -> new HttpClientHandler(ServerCertificateCustomValidationCallback = (fun a b c d -> true)))
      .BuildSingleton<HttpClient, HttpClientHandler>(fun handler -> new HttpClient(handler))
      .BuildSingleton<ITelegramBotClient, TelegramSettings, HttpClient>(fun settings client ->
        let options = TelegramBotClientOptions(settings.Token, settings.ApiUrl)
        TelegramBotClient(options, client) :> ITelegramBotClient)

    services
      .BuildSingleton<UserConversion.Load, IMongoDatabase>(UserConversion.load)
      .BuildSingleton<UserConversion.Save, IMongoDatabase>(UserConversion.save)

      .BuildSingleton<DeleteBotMessage, ITelegramBotClient>(deleteBotMessage)
      .BuildSingleton<ReplyWithVideo, WorkersSettings, ITelegramBotClient>(replyWithVideo)
      .BuildSingleton<Translation.LoadTranslations, IMongoDatabase, ILoggerFactory>(Translation.loadTranslations)

      .BuildSingleton<Chat.LoadTranslations, User.Load, Translation.LoadTranslations>(Chat.loadTranslations)

      .BuildSingleton<User.Load, IMongoDatabase>(User.load)
      .BuildSingleton<User.EnsureExists, IMongoDatabase, ILoggerFactory>(User.ensureExists)

      .BuildSingleton<Conversion.New.InputFile.DownloadDocument, ITelegramBotClient, WorkersSettings>(Conversion.New.InputFile.downloadDocument)

      .BuildSingleton<ParseCommand, InputValidationSettings>(parseCommand)
