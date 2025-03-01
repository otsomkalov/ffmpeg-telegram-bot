#nowarn "20"

namespace Telegram.Infrastructure

open System.Net.Http
open Domain.Repos
open Infrastructure.Settings
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open MongoDB.Bson.Serialization
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
      .BuildSingleton<IMongoCollection<Entities.User>, IMongoDatabase>(_.GetCollection("users"))
      .BuildSingleton<IMongoCollection<Entities.Channel>, IMongoDatabase>(_.GetCollection("channels"))
      .BuildSingleton<IMongoCollection<Entities.Group>, IMongoDatabase>(_.GetCollection("groups"))
      .BuildSingleton<IMongoCollection<Entities.Translation>, IMongoDatabase>(_.GetCollection("resources"))

    services

      .AddSingleton<IUserRepo, UserRepo>()
      .AddSingleton<IUserConversionRepo, UserConversionRepo>()

      .AddSingleton<IChannelRepo, ChannelRepo>()
      .AddSingleton<IGroupRepo, GroupRepo>()

      .BuildSingleton<DeleteBotMessage, ITelegramBotClient>(deleteBotMessage)
      .BuildSingleton<ReplyWithVideo, WorkersSettings, ITelegramBotClient>(replyWithVideo)
      .BuildSingleton<Translation.LoadDefaultTranslations, IMongoCollection<Entities.Translation>, ILoggerFactory>(
        Translation.loadDefaultTranslations
      )
      .BuildSingleton<
        Translation.LoadTranslations,
        IMongoCollection<Entities.Translation>,
        ILoggerFactory,
        Translation.LoadDefaultTranslations
        >(
        Translation.loadTranslations
      )

      .BuildSingleton<User.LoadTranslations, IUserRepo, Translation.LoadTranslations, Translation.LoadDefaultTranslations>(
        User.loadTranslations
      )

      .BuildSingleton<ParseCommand, InputValidationSettings>(parseCommand)