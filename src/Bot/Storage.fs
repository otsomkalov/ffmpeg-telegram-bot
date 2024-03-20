[<RequireQualifiedAccess>]
module Bot.Storage

open Azure.Storage.Blobs
open Infrastructure.Settings
open otsom.fs.Extensions

let deleteVideo (workersSettings: WorkersSettings) =
  fun name ->
    let blobService = BlobServiceClient(workersSettings.ConnectionString)

    let convertedFilesContainer =
      blobService.GetBlobContainerClient(workersSettings.Converter.Output.Container)

    let convertedFileBlob = convertedFilesContainer.GetBlobClient(name)
    convertedFileBlob.DeleteIfExistsAsync() |> Task.ignore

let deleteThumbnail (workersSettings: WorkersSettings) =
  fun name ->
    let blobService = BlobServiceClient(workersSettings.ConnectionString)

    let convertedFilesContainer =
      blobService.GetBlobContainerClient(workersSettings.Thumbnailer.Output.Container)

    let convertedFileBlob = convertedFilesContainer.GetBlobClient(name)

    convertedFileBlob.DeleteIfExistsAsync() |> Task.ignore
