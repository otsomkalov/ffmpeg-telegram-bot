namespace Bot

open Azure.Monitor.OpenTelemetry.Exporter
open OpenTelemetry
open System.Reflection
open System.Text.Json
open System.Text.Json.Serialization
open Infrastructure.Helpers
open Microsoft.Azure.Functions.Worker.OpenTelemetry
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Azure.Functions.Worker
open Microsoft.Extensions.Logging
open Infrastructure
open OpenTelemetry.Metrics
open OpenTelemetry.Trace
open Telegram.Infrastructure
open Domain
open Telegram

#nowarn "20"

module Startup =
  let configureWebApp (builder: IFunctionsWorkerApplicationBuilder) =
    builder.Services.Configure<JsonSerializerOptions>(fun opts -> JSON.options.AddToJsonSerializerOptions(opts))

    ()

  let configureAppConfiguration _ (configBuilder: IConfigurationBuilder) =

    configBuilder.AddUserSecrets(Assembly.GetExecutingAssembly(), true)

    ()

  let configureLogging (builder: ILoggingBuilder) =

    builder.AddOpenTelemetry(fun b ->
      b.IncludeScopes <- true
      b.IncludeFormattedMessage <- true
      ())

    ()

  let private configureServices (ctx: HostBuilderContext) (services: IServiceCollection) =
    services
      .AddOpenTelemetry()
      .UseFunctionsWorkerDefaults()
      .WithTracing(fun builder ->
        builder.AddSource(Observability.ActivitySource.Name)

        builder.AddHttpClientInstrumentation(fun options ->
          options.FilterHttpRequestMessage <- _.RequestUri.AbsolutePath.StartsWith("/admin") >> not
          ())

        ())
      .UseAzureMonitorExporter()

    services
    |> Startup.addDomain
    |> Startup.addInfra ctx.Configuration
    |> Startup.addTelegram ctx.Configuration
    |> Startup.addTelegramInfra ctx.Configuration

    services.ConfigureTelegramBot<Microsoft.AspNetCore.Http.Json.JsonOptions>(_.SerializerOptions)

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