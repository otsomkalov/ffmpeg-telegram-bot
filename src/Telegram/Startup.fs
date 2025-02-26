module Telegram.Startup

open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Telegram.Core
open Telegram.Repos
open Telegram.Workflows
open otsom.fs.Extensions.DependencyInjection

let addTelegram (cfg: IConfiguration) (services: IServiceCollection) =
  services

    .BuildSingleton<User.BuildResourceProvider, _, _>(User.createResourceProvider)