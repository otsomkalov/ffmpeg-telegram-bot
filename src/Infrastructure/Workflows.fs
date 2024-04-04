namespace Infrastructure

open System
open System.Net
open System.Net.Http
open Azure.Storage.Blobs
open Domain.Core
open Infrastructure.Helpers
open Infrastructure.Settings
open MongoDB.Driver
open otsom.fs.Extensions
open Domain.Repos
open Infrastructure.Mappings
open System.Threading.Tasks
open Infrastructure.Core

[<RequireQualifiedAccess>]
module Conversion =
  [<RequireQualifiedAccess>]
  module New =
    [<RequireQualifiedAccess>]
    module InputFile =
      let downloadLink (httpClientFactory: IHttpClientFactory) (workersSettings: WorkersSettings) : Conversion.New.InputFile.DownloadLink =
        let getBlobStream = Storage.getBlobStream workersSettings

        fun link ->
          task {
            use client = httpClientFactory.CreateClient()
            use request = new HttpRequestMessage(HttpMethod.Get, link.Url)
            use! response = client.SendAsync(request)

            return!
              match response.StatusCode with
              | HttpStatusCode.Unauthorized -> Conversion.New.DownloadLinkError.Unauthorized |> Error |> Task.FromResult
              | HttpStatusCode.NotFound -> Conversion.New.DownloadLinkError.NotFound |> Error |> Task.FromResult
              | HttpStatusCode.InternalServerError -> Conversion.New.DownloadLinkError.ServerError |> Error |> Task.FromResult
              | _ ->
                task {
                  let fileName = link.Url |> Uri |> (_.Segments) |> Seq.last

                  use! converterBlobStream = getBlobStream fileName workersSettings.Converter.Input.Container
                  use! thumbnailerBlobStream = getBlobStream fileName workersSettings.Thumbnailer.Input.Container

                  do! response.Content.CopyToAsync(converterBlobStream)
                  do! response.Content.CopyToAsync(thumbnailerBlobStream)

                  return Ok(fileName)
                }
          }

  [<RequireQualifiedAccess>]
  module Converted =
    let save (db: IMongoDatabase) : Conversion.Converted.Save =
      let collection = db.GetCollection "conversions"

      fun conversion ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), (conversion.Id |> ConversionId.value))
        let entity = conversion |> Conversion.Converted.toDb
        collection.ReplaceOneAsync(filter, entity) |> Task.ignore

  [<RequireQualifiedAccess>]
  module Thumbnailed =
    let save (db: IMongoDatabase) : Conversion.Thumbnailed.Save =
      let collection = db.GetCollection "conversions"

      fun conversion ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), (conversion.Id |> ConversionId.value))
        let entity = conversion |> Conversion.Thumbnailed.toDb
        collection.ReplaceOneAsync(filter, entity) |> Task.ignore

  [<RequireQualifiedAccess>]
  module PreparedOrConverted =
    let load (db: IMongoDatabase) : Conversion.PreparedOrConverted.Load =
      let collection = db.GetCollection "conversions"

      fun conversionId ->
        let (ConversionId conversionId) = conversionId
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Conversion.PreparedOrConverted.fromDb

  [<RequireQualifiedAccess>]
  module PreparedOrThumbnailed =
    let load (db: IMongoDatabase) : Conversion.PreparedOrThumbnailed.Load =
      let collection = db.GetCollection "conversions"

      fun conversionId ->
        let (ConversionId conversionId) = conversionId
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Conversion.PreparedOrThumbnailed.fromDb

  [<RequireQualifiedAccess>]
  module Completed =
    let deleteVideo (settings: WorkersSettings) : Conversion.Completed.DeleteVideo =
      let blobServiceClient = BlobServiceClient(settings.ConnectionString)

      let container =
        blobServiceClient.GetBlobContainerClient settings.Converter.Output.Container

      fun name ->
        let (Conversion.Video name) = name
        let blob = container.GetBlobClient(name)
        blob.DeleteAsync() |> Task.ignore

    let deleteThumbnail (settings: WorkersSettings) : Conversion.Completed.DeleteThumbnail =
      let blobServiceClient = BlobServiceClient(settings.ConnectionString)

      let container =
        blobServiceClient.GetBlobContainerClient settings.Thumbnailer.Output.Container

      fun name ->
        let (Conversion.Thumbnail name) = name
        let blob = container.GetBlobClient(name)
        blob.DeleteAsync() |> Task.ignore

    let load (db: IMongoDatabase) : Conversion.Completed.Load =
      let collection = db.GetCollection "conversions"

      fun conversionId ->
        let (ConversionId conversionId) = conversionId
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Conversion.Completed.fromDb

    let save (db: IMongoDatabase) : Conversion.Completed.Save =
      let collection = db.GetCollection "conversions"

      fun conversion ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), (conversion.Id |> ConversionId.value))
        let entity = conversion |> Conversion.Completed.toDb

        collection.ReplaceOneAsync(filter, entity)
        |> Task.map (fun _ -> conversion)
