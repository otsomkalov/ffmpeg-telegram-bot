namespace Telegram.Infrastructure

open Domain.Repos
open Infrastructure.Settings
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open MongoDB.Driver
open Telegram.Bot
open Telegram.Core
open Telegram.Infrastructure.Workflows
open Telegram.Workflows
open otsom.fs.Extensions.DependencyInjection

module Startup =
  let addTelegram (services: IServiceCollection) =
    services
      .BuildSingleton<UserConversion.Load, IMongoDatabase>(UserConversion.load)
      .BuildSingleton<DeleteBotMessage, ITelegramBotClient>(deleteBotMessage)
      .BuildSingleton<ReplyWithVideo, WorkersSettings, ITelegramBotClient>(replyWithVideo)
      .BuildSingleton<Translation.GetLocaleTranslations, IMongoDatabase, ILoggerFactory>(Translation.getLocaleTranslations)
      .BuildSingleton<User.Load, IMongoDatabase>(User.load)
      .BuildSingleton<Conversion.New.InputFile.DownloadDocument, ITelegramBotClient, WorkersSettings>(Conversion.New.InputFile.downloadDocument)
