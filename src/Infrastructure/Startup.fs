#nowarn "20"

namespace Infrastructure

open System
open System.Net.Http
open Domain
open Infrastructure.Settings
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open MongoDB.Driver
open Polly.Extensions.Http
open otsom.fs.Extensions.DependencyInjection
open Polly

module Startup =
  [<Literal>]
  let private customUserAgent =
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:139.0) Gecko/20100101 Firefox/139.0"

  let private retryPolicy =
    HttpPolicyExtensions
      .HandleTransientHttpError()
      .WaitAndRetryAsync(5, (fun retryAttempt -> TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))))

  let private configureMongoClient loggerFactory (settings: DatabaseSettings) =
    let mongoClientSettings = MongoClientSettings.FromConnectionString settings.ConnectionString


    new MongoClient(mongoClientSettings) :> IMongoClient

  let private configureMongoDatabase (settings: DatabaseSettings) (mongoClient: IMongoClient) =
    mongoClient.GetDatabase(settings.Name)

  let addInfra (cfg: IConfiguration) (services: IServiceCollection) =
    services
      .AddHttpClient(fun (client: HttpClient) -> client.DefaultRequestHeaders.UserAgent.ParseAdd(customUserAgent))
      .AddPolicyHandler(retryPolicy)

    services
      .BuildSingleton<IMongoClient, ILoggerFactory, DatabaseSettings>(configureMongoClient)
      .BuildSingleton<IMongoDatabase, DatabaseSettings, IMongoClient>(configureMongoDatabase)

      .BuildSingleton<IMongoCollection<Entities.Conversion>, IMongoDatabase>(_.GetCollection("conversions"))

    services.Configure<WorkersSettings>(cfg.GetSection WorkersSettings.SectionName)

    services
      .BuildSingleton<WorkersSettings, IOptions<WorkersSettings>>(_.Value)
      .BuildSingleton<DatabaseSettings, IConfiguration>(fun cfg -> cfg.GetSection(DatabaseSettings.SectionName).Get<DatabaseSettings>())

    services.AddSingleton<IConversionRepo, ConversionRepo>()