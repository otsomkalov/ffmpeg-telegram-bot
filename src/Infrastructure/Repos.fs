namespace Infrastructure

open Azure.Storage.Blobs
open Domain
open Domain.Core
open Infrastructure.Core
open Infrastructure.Settings
open Microsoft.Extensions.Options
open MongoDB.Driver
open MongoDB.Driver.Linq
open otsom.fs.Extensions

type ConversionRepo(collection: IMongoCollection<Entities.Conversion>, options: IOptions<WorkersSettings>) =
  let settings = options.Value
  let blobServiceClient = BlobServiceClient(settings.ConnectionString)

  interface IConversionRepo with
    member _.LoadConversion(ConversionId id) =
      collection.AsQueryable().FirstOrDefaultAsync(fun c -> c.Id = id) &|> _.ToDomain

    member _.SaveConversion conversion =
      let filter = Builders<Entities.Conversion>.Filter.Eq(_.Id, conversion.Id.Value)

      collection.ReplaceOneAsync(filter, Entities.Conversion.FromDomain conversion, ReplaceOptions(IsUpsert = true))
      &|> ignore

    member this.DeleteThumbnail(Conversion.Thumbnail thumbnail) =
      let container =
        blobServiceClient.GetBlobContainerClient settings.Thumbnailer.Output.Container

      let blob = container.GetBlobClient(thumbnail)

      blob.DeleteAsync() &|> ignore

    member this.DeleteVideo(Conversion.Video video) =
      let container =
        blobServiceClient.GetBlobContainerClient settings.Converter.Output.Container

      let blob = container.GetBlobClient(video)

      blob.DeleteAsync() &|> ignore