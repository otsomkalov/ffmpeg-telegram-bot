namespace Bot

open System
open System.Net.Http
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open Infrastructure.Helpers
open Infrastructure.Settings
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Azure.Functions.Worker
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.ApplicationInsights
open MongoDB.ApplicationInsights
open MongoDB.Driver
open Telegram.Bot
open MongoDB.ApplicationInsights.DependencyInjection
open otsom.fs.Extensions.DependencyInjection
open otsom.fs.Telegram.Bot
open Infrastructure
open Telegram.Infrastructure

#nowarn "20"

module Startup =
  let configureWebApp (builder: IFunctionsWorkerApplicationBuilder) =
    builder.Services.Configure<JsonSerializerOptions>(fun opts -> JSON.options.AddToJsonSerializerOptions(opts))

    ()

  let configureAppConfiguration _ (configBuilder: IConfigurationBuilder) =

    configBuilder.AddUserSecrets(Assembly.GetExecutingAssembly(), true)

    ()

  let configureLogging (builder: ILoggingBuilder) =
    builder.AddFilter<ApplicationInsightsLoggerProvider>(String.Empty, LogLevel.Information)

    ()

  let private configureMongoClient (factory: IMongoClientFactory) (settings: Settings.DatabaseSettings) =
    factory.GetClient(settings.ConnectionString)

  let private configureMongoDatabase (settings: Settings.DatabaseSettings) (mongoClient: IMongoClient) =
    mongoClient.GetDatabase(settings.Name)

  let private configureServices _ (services: IServiceCollection) =
    services.AddApplicationInsightsTelemetryWorkerService()
    services.ConfigureFunctionsApplicationInsights()

    services
    |> Startup.addTelegramBotCore
    |> Startup.addDomain
    |> Startup.addTelegram

    services
      .BuildSingleton<WorkersSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(WorkersSettings.SectionName)
          .Get<WorkersSettings>())
      .BuildSingleton<Settings.TelegramSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(Settings.TelegramSettings.SectionName)
          .Get<Settings.TelegramSettings>())
      .BuildSingleton<Settings.DatabaseSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(Settings.DatabaseSettings.SectionName)
          .Get<Settings.DatabaseSettings>())
      .BuildSingleton<Settings.InputValidationSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(Settings.InputValidationSettings.SectionName)
          .Get<Settings.InputValidationSettings>())

    services
      .AddMongoClientFactory()
      .BuildSingleton<IMongoClient, IMongoClientFactory, Settings.DatabaseSettings>(configureMongoClient)
      .BuildSingleton<IMongoDatabase, Settings.DatabaseSettings, IMongoClient>(configureMongoDatabase)
      .AddSingleton<HttpClientHandler>(fun _ -> new HttpClientHandler(ServerCertificateCustomValidationCallback = (fun a b c d -> true)))
      .BuildSingleton<HttpClient, HttpClientHandler>(fun handler -> new HttpClient(handler))
      .BuildSingleton<ITelegramBotClient, Settings.TelegramSettings, HttpClient>(fun settings client ->
        let options = TelegramBotClientOptions(settings.Token, settings.ApiUrl)
        TelegramBotClient(options, client) :> ITelegramBotClient)

    services.AddMvcCore().AddNewtonsoftJson()

    ()

  let host =
    HostBuilder()
      .ConfigureFunctionsWebApplication(configureWebApp)
      .ConfigureAppConfiguration(configureAppConfiguration)
      .ConfigureLogging(configureLogging)
      .ConfigureServices(configureServices)
      .Build()

  // If using the Cosmos, Blob or Tables extension, you will need configure the extensions manually using the extension methods below.
  // Learn more about this here: https://go.microsoft.com/fwlink/?linkid=2245587
  // ConfigureFunctionsWorkerDefaults(fun (context: HostBuilderContext) (appBuilder: IFunctionsWorkerApplicationBuilder) ->
  //     appBuilder.ConfigureCosmosDBExtension() |> ignore
  //     appBuilder.ConfigureBlobStorageExtension() |> ignore
  //     appBuilder.ConfigureTablesExtension() |> ignore
  // ) |> ignore


  host.Run()
