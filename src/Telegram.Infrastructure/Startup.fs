#nowarn "20"

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

module Startup =
  let addTelegram (services: IServiceCollection) =
    services
      .BuildSingleton<InputValidationSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(InputValidationSettings.SectionName)
          .Get<InputValidationSettings>())
      .BuildSingleton<TelegramSettings, IConfiguration>(fun cfg -> cfg.GetSection(TelegramSettings.SectionName).Get<TelegramSettings>())

    services
      .AddSingleton<HttpClientHandler>(fun _ -> new HttpClientHandler(ServerCertificateCustomValidationCallback = (fun a b c d -> true)))
      .BuildSingleton<HttpClient, HttpClientHandler>(fun handler -> new HttpClient(handler))
      .BuildSingleton<ITelegramBotClient, TelegramSettings, HttpClient>(fun settings client ->
        let options = TelegramBotClientOptions(settings.Token, settings.ApiUrl)
        TelegramBotClient(options, client) :> ITelegramBotClient)

    services
      .BuildSingleton<IMongoCollection<Database.User>, IMongoDatabase>(_.GetCollection<Database.User>("users"))
      .BuildSingleton<IMongoCollection<Database.Channel>, IMongoDatabase>(_.GetCollection<Database.Channel>("channels"))
      .BuildSingleton<IMongoCollection<Database.Group>, IMongoDatabase>(_.GetCollection<Database.Group>("groups"))
      .BuildSingleton<IMongoCollection<Database.Translation>, IMongoDatabase>(_.GetCollection<Database.Translation>("resources"))

    services

      .AddSingleton<IUserRepo, UserRepo>()
      .AddSingleton<IUserConversionRepo, UserConversionRepo>()

      .AddSingleton<IChannelRepo, ChannelRepo>()
      .AddSingleton<IGroupRepo, GroupRepo>()

      .BuildSingleton<DeleteBotMessage, ITelegramBotClient>(deleteBotMessage)
      .BuildSingleton<ReplyWithVideo, WorkersSettings, ITelegramBotClient>(replyWithVideo)
      .BuildSingleton<Translation.LoadDefaultTranslations, IMongoCollection<Database.Translation>, ILoggerFactory>(
        Translation.loadDefaultTranslations
      )
      .BuildSingleton<
        Translation.LoadTranslations,
        IMongoCollection<Database.Translation>,
        ILoggerFactory,
        Translation.LoadDefaultTranslations
        >(
        Translation.loadTranslations
      )

      .BuildSingleton<User.LoadTranslations, IUserRepo, Translation.LoadTranslations, Translation.LoadDefaultTranslations>(
        User.loadTranslations
      )

      .BuildSingleton<Conversion.New.InputFile.DownloadDocument, ITelegramBotClient, WorkersSettings>(
        Conversion.New.InputFile.downloadDocument
      )

      .BuildSingleton<ParseCommand, InputValidationSettings>(parseCommand)