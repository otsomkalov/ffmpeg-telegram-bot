namespace Bot

open System
open System.Net
open System.Net.Http
open System.Net.Security
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open FSharp
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Azure.Functions.Worker
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.ApplicationInsights
open MongoDB.ApplicationInsights
open MongoDB.Driver
open Polly.Extensions.Http
open Telegram.Bot
open MongoDB.ApplicationInsights.DependencyInjection
open Polly
open otsom.FSharp.Extensions.ServiceCollection
open Helpers

#nowarn "20"

module Startup =
  ServicePointManager.ServerCertificateValidationCallback <- RemoteCertificateValidationCallback(fun o c ch er -> true)

  let configureWebApp (builder: IFunctionsWorkerApplicationBuilder) =
    builder.Services.Configure<JsonSerializerOptions>(fun opts -> JSON.options.AddToJsonSerializerOptions(opts))

    ()

  let configureAppConfiguration _ (configBuilder: IConfigurationBuilder) =

    configBuilder.AddUserSecrets(Assembly.GetExecutingAssembly(), true)

    ()

  let configureLogging (builder: ILoggingBuilder) =
    builder.AddFilter<ApplicationInsightsLoggerProvider>(String.Empty, LogLevel.Information)

    ()

  [<Literal>]
  let chromeUserAgent =
    "Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.102 Safari/537.36"

  let private retryPolicy =
    HttpPolicyExtensions
      .HandleTransientHttpError()
      .WaitAndRetryAsync(5, (fun retryAttempt -> TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))))

  let private configureServices (context: HostBuilderContext) (services: IServiceCollection) =
    services.AddApplicationInsightsTelemetryWorkerService()
    services.ConfigureFunctionsApplicationInsights()

    services
      .AddSingletonFunc<Settings.WorkersSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(Settings.WorkersSettings.SectionName)
          .Get<Settings.WorkersSettings>())
      .AddSingletonFunc<Settings.TelegramSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(Settings.TelegramSettings.SectionName)
          .Get<Settings.TelegramSettings>())
      .AddSingletonFunc<Settings.DatabaseSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(Settings.DatabaseSettings.SectionName)
          .Get<Settings.DatabaseSettings>())

    services.AddMongoClientFactory()

    services
      .AddSingletonFunc<IMongoClient, IMongoClientFactory, Settings.DatabaseSettings>(fun factory settings ->
        factory.GetClient settings.ConnectionString)
      .AddSingletonFunc<IMongoDatabase, IMongoClient, Settings.DatabaseSettings>(fun client settings -> client.GetDatabase settings.Name)
      .AddSingletonFunc<ITelegramBotClient, Settings.TelegramSettings>(fun settings ->
        TelegramBotClientOptions(settings.Token, settings.ApiUrl) |> TelegramBotClient :> ITelegramBotClient)

    services
      .AddHttpClient(fun (client: HttpClient) -> client.DefaultRequestHeaders.UserAgent.ParseAdd(chromeUserAgent))
      .AddPolicyHandler(retryPolicy)

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
