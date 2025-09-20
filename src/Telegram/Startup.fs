module Telegram.Startup

#nowarn "20"

open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open otsom.fs.Resources

let addTelegram (cfg: IConfiguration) (services: IServiceCollection) =

  services
  |> Startup.addResources cfg

  services
    .AddSingleton<IChatSvc, ChatSvc>()
    .AddSingleton<IFFMpegBot, FFMpegBot>()
