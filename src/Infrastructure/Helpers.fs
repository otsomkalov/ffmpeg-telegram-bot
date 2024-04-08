namespace Infrastructure

open System.Text.Json
open System.Text.Json.Serialization
open Azure.Storage.Blobs
open Infrastructure.Settings

module Helpers =
  [<RequireQualifiedAccess>]
  module JSON =
    let options =
      JsonFSharpOptions.Default().WithUnionExternalTag().WithUnionUnwrapRecordCases()

    let private options' = options.ToJsonSerializerOptions()

    let serialize value =
      JsonSerializer.Serialize(value, options')


  [<RequireQualifiedAccess>]
  module Storage =
    let getBlobStream (workersSettings: WorkersSettings) =
      fun name container ->
        let blobServiceClient = BlobServiceClient(workersSettings.ConnectionString)

        let containerClient = blobServiceClient.GetBlobContainerClient(container)

        let blobClient = containerClient.GetBlobClient(name)

        blobClient.OpenWriteAsync(true)
