module Telegram.Startup

#nowarn "20"

open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Telegram.Core
open Telegram.Repos
open Telegram.Workflows
open otsom.fs.Extensions.DependencyInjection
open otsom.fs.Resources

let addTelegram (cfg: IConfiguration) (services: IServiceCollection) =

  services
  |> Startup.addResources cfg

  services
    .BuildSingleton<Resources.LoadResources, _, _>(Resources.loadResources)
    .BuildSingleton<User.LoadResources, IUserRepo, _>(User.loadResources)