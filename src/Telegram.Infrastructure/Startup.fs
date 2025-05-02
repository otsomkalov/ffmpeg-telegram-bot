#nowarn "20"

namespace Telegram.Infrastructure

open System.Net.Http
open Infrastructure.Settings
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open MongoDB.Driver
open Telegram.Bot
open Telegram.Core
open Telegram.Infrastructure.Settings
open Telegram.Infrastructure.Workflows
open Telegram.Workflows
open otsom.fs.Extensions.DependencyInjection
open Telegram.Repos
open otsom.fs.Resources.Mongo

module Startup =
  let addTelegramInfra (cfg: IConfiguration) (services: IServiceCollection) =
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

    services
    |> Startup.addMongoResources cfg

    services

      .AddSingleton<IUserRepo, UserRepo>()
      .AddSingleton<IUserConversionRepo, UserConversionRepo>()

      .AddSingleton<IChannelRepo, ChannelRepo>()
      .AddSingleton<IGroupRepo, GroupRepo>()

      .BuildSingleton<DeleteBotMessage, ITelegramBotClient>(deleteBotMessage)
      .BuildSingleton<ReplyWithVideo, WorkersSettings, ITelegramBotClient>(replyWithVideo)

      .BuildSingleton<ParseCommand, InputValidationSettings>(parseCommand)