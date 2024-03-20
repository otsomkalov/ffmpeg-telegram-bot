namespace Infrastructure

open Azure.Storage.Blobs
open Domain.Workflows
open Infrastructure.Settings
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
          let blob = container.GetBlobClient(name)
          blob.DeleteAsync() |> Task.ignore

      let deleteThumbnail (settings: WorkersSettings) : Conversion.Completed.DeleteThumbnail =
        let blobServiceClient = BlobServiceClient(settings.ConnectionString)

        let container =
          blobServiceClient.GetBlobContainerClient settings.Thumbnailer.Output.Container

        fun name ->
          let blob = container.GetBlobClient(name)
          blob.DeleteAsync() |> Task.ignore
