#nowarn "20"

namespace Telegram.Infrastructure

open System.Net.Http
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open MongoDB.Driver
open Telegram
open Telegram.Bot
open Telegram.Infrastructure.Services
open Telegram.Infrastructure.Settings
open otsom.fs.Extensions.DependencyInjection
open Telegram.Repos
open otsom.fs.Resources.Mongo
open otsom.fs.Bot.Telegram

module Startup =
  let buildExtendedBotService workerOptions bot : BuildExtendedBotService =
    fun chatId -> ExtendedBotService(workerOptions, bot, chatId)

  let addTelegramInfra (cfg: IConfiguration) (services: IServiceCollection) =
    services
      .BuildSingleton<TelegramSettings, IConfiguration>(fun cfg -> cfg.GetSection(TelegramSettings.SectionName).Get<TelegramSettings>())

    services
      .AddSingleton<HttpClientHandler>(fun _ -> new HttpClientHandler(ServerCertificateCustomValidationCallback = (fun a b c d -> true)))
      .BuildSingleton<HttpClient, HttpClientHandler>(fun handler -> new HttpClient(handler))
      .BuildSingleton<ITelegramBotClient, TelegramSettings, HttpClient>(fun settings client ->
        let options = TelegramBotClientOptions(settings.Token, settings.ApiUrl)
        TelegramBotClient(options, client) :> ITelegramBotClient)

    services
      .BuildSingleton<IMongoCollection<Entities.Chat>, IMongoDatabase>(_.GetCollection("chats"))

    services |> Startup.addMongoResources cfg |> Startup.addTelegramBot cfg

    services

      .AddSingleton<IUserConversionRepo, UserConversionRepo>()

      .AddSingleton<IChatRepo, ChatRepo>()

      .BuildSingleton<BuildExtendedBotService, _, _>(buildExtendedBotService)