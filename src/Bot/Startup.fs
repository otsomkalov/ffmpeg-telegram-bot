module Bot.Startup

#nowarn "20"

open Bot.Handlers
open Bot.Settings
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open otsom.fs.Extensions.DependencyInjection
open otsom.fs.Resources

let private addHandlers (services: IServiceCollection) =
  services
    .AddSingleton<MsgHandlerFactory>(startHandler)
    .BuildSingleton<MsgHandlerFactory, _, _, _, _, _>(linksHandler)
    .BuildSingleton<MsgHandlerFactory, _, _, _, _, _>(documentHandler)
    .BuildSingleton<MsgHandlerFactory, _, _, _, _, _>(videoHandler)

let addTelegram (cfg: IConfiguration) (services: IServiceCollection) =
  services.BuildSingleton<InputValidationSettings, IConfiguration>(
    _.GetSection(InputValidationSettings.SectionName).Get<InputValidationSettings>()
  )

  services |> Startup.addResources cfg |> addHandlers

  services.AddSingleton<IChatSvc, ChatSvc>().AddSingleton<IFFMpegBot, FFMpegBot>()