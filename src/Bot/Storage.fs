[<RequireQualifiedAccess>]
module Bot.Storage

open Azure.Storage.Blobs
open otsom.fs.Extensions

let deleteVideo (workersSettings: Settings.WorkersSettings) =
  fun name ->
    let blobService = BlobServiceClient(workersSettings.ConnectionString)

    let convertedFilesContainer =
      blobService.GetBlobContainerClient(workersSettings.Converter.Output.Container)

    let convertedFileBlob = convertedFilesContainer.GetBlobClient(name)
    convertedFileBlob.DeleteIfExistsAsync() |> Task.ignore

let deleteThumbnail (workersSettings: Settings.WorkersSettings) =
  fun name ->
    let blobService = BlobServiceClient(workersSettings.ConnectionString)

    let convertedFilesContainer =
      blobService.GetBlobContainerClient(workersSettings.Thumbnailer.Output.Container)

    let convertedFileBlob = convertedFilesContainer.GetBlobClient(name)
    convertedFileBlob.DeleteIfExistsAsync() |> Task.ignore
