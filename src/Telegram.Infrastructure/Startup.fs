namespace Telegram.Infrastructure

open Infrastructure.Settings
open Microsoft.Extensions.DependencyInjection
open MongoDB.Driver
open Telegram.Bot
open Telegram.Infrastructure.Workflows
open Telegram.Workflows
open otsom.fs.Extensions.DependencyInjection

module Startup =
  let addTelegram (services: IServiceCollection) =
    services
      .BuildSingleton<UserConversion.Load, IMongoDatabase>(UserConversion.load)
      .BuildSingleton<DeleteBotMessage, ITelegramBotClient>(deleteBotMessage)
      .BuildSingleton<ReplyWithVideo, WorkersSettings, ITelegramBotClient>(replyWithVideo)

