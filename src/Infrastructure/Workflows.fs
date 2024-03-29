namespace Infrastructure

open Azure.Storage.Blobs
open Domain.Core
open Domain.Workflows
open Infrastructure.Settings
open MongoDB.Driver
open otsom.fs.Extensions

module Workflows =

  [<RequireQualifiedAccess>]
  module Conversion =
    [<RequireQualifiedAccess>]
    module Completed =
      let deleteVideo (settings: WorkersSettings) : Conversion.Completed.DeleteVideo =
        let blobServiceClient = BlobServiceClient(settings.ConnectionString)

        let container =
          blobServiceClient.GetBlobContainerClient settings.Converter.Output.Container

        fun name ->
          let (Video name) = name
          let blob = container.GetBlobClient(name)
          blob.DeleteAsync() |> Task.ignore

      let deleteThumbnail (settings: WorkersSettings) : Conversion.Completed.DeleteThumbnail =
        let blobServiceClient = BlobServiceClient(settings.ConnectionString)

        let container =
          blobServiceClient.GetBlobContainerClient settings.Thumbnailer.Output.Container

        fun name ->
          let (Thumbnail name) = name
          let blob = container.GetBlobClient(name)
          blob.DeleteAsync() |> Task.ignore

      let load (db: IMongoDatabase) : Conversion.Completed.Load =
        let collection = db.GetCollection "conversions"

        fun conversionId ->
          let (ConversionId conversionId) = conversionId
          let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

          collection.Find(filter).SingleOrDefaultAsync()
          |> Task.map Mappings.Conversion.Completed.fromDb
