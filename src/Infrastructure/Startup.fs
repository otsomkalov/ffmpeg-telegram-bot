#nowarn "20"

namespace Infrastructure

open System
open System.Net.Http
open Domain
open Domain.Core
open Domain.Workflows
open Infrastructure.Settings
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Options
open MongoDB.ApplicationInsights
open MongoDB.Driver
open Polly.Extensions.Http
open otsom.fs.Extensions.DependencyInjection
open Queue
open Domain.Repos
open Polly
open Infrastructure.Workflows
open MongoDB.ApplicationInsights.DependencyInjection

module Startup =
  [<Literal>]
  let private chromeUserAgent =
    "Mozilla/5.0 (Windows NT 10.0) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/98.0.4758.102 Safari/537.36"

  let private retryPolicy =
    HttpPolicyExtensions
      .HandleTransientHttpError()
      .WaitAndRetryAsync(5, (fun retryAttempt -> TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))))

  let private configureMongoClient (factory: IMongoClientFactory) (settings: DatabaseSettings) =
    factory.GetClient(settings.ConnectionString)

  let private configureMongoDatabase (settings: DatabaseSettings) (mongoClient: IMongoClient) =
    mongoClient.GetDatabase(settings.Name)

  let addInfra (cfg: IConfiguration) (services: IServiceCollection) =
    services
      .AddHttpClient(fun (client: HttpClient) -> client.DefaultRequestHeaders.UserAgent.ParseAdd(chromeUserAgent))
      .AddPolicyHandler(retryPolicy)

    services
      .AddMongoClientFactory()
      .BuildSingleton<IMongoClient, IMongoClientFactory, DatabaseSettings>(configureMongoClient)
      .BuildSingleton<IMongoDatabase, DatabaseSettings, IMongoClient>(configureMongoDatabase)

      .BuildSingleton<IMongoCollection<Entities.Conversion>, IMongoDatabase>(_.GetCollection("conversions"))

    services.Configure<WorkersSettings>(cfg.GetSection WorkersSettings.SectionName)

    services
      .BuildSingleton<WorkersSettings, IOptions<WorkersSettings>>(_.Value)
      .BuildSingleton<DatabaseSettings, IConfiguration>(fun cfg ->
        cfg
          .GetSection(DatabaseSettings.SectionName)
          .Get<DatabaseSettings>())

    services.AddSingleton<IConversionRepo, ConversionRepo>()

    services
      .AddSingleton<ConversionId.Generate>(ConversionId.generate)
      .BuildSingleton<Conversion.Create, ConversionId.Generate, IConversionRepo>(Conversion.create)

      .BuildSingleton<Conversion.New.QueuePreparation, WorkersSettings>(Conversion.New.queuePreparation)
      .BuildSingleton<Conversion.New.InputFile.DownloadLink, IHttpClientFactory, WorkersSettings>(Conversion.New.InputFile.downloadLink)

      // TODO: Functions of same type. How to register?
      // .BuildSingleton<Conversion.Prepared.QueueConversion, WorkersSettings>(Conversion.Prepared.queueConversion)
      // .BuildSingleton<Conversion.Prepared.QueueThumbnailing, WorkersSettings>(Conversion.Prepared.queueThumbnailing)