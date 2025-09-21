module Telegram.Startup

#nowarn "20"

open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Telegram.Repos
open Telegram.Workflows
open otsom.fs.Extensions.DependencyInjection
open otsom.fs.Resources

let addTelegram (cfg: IConfiguration) (services: IServiceCollection) =

  services
  |> Startup.addResources cfg

  services
    .AddSingleton<IChatSvc, ChatSvc>()
    .AddSingleton<IFFMpegBot, FFMpegBot>()
    .BuildSingleton<Chat.LoadResources, IChatRepo, _, _>(Chat.loadResources)