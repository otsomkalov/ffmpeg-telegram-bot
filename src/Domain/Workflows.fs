namespace Domain

open System.Threading.Tasks
open Domain.Core
open Domain.Core.Conversion
open Microsoft.FSharp.Core
open otsom.fs.Extensions
open FsToolkit.ErrorHandling

module Workflows =
  [<RequireQualifiedAccess>]
  module Conversion =
    let create (repo: #ISaveConversion & #IGenerateConversionId) : Create =
      fun () ->
        task {
          let newConversion: Conversion.New = { Id = repo.GenerateConversionId() }

          do! repo.SaveConversion(Conversion.New newConversion)

          return newConversion
        }

type ConversionService(repo: IConversionRepo) =
  interface IConversionService with
    member this.CleanupConversion(conversion) =
      task {
        do! repo.DeleteVideo conversion.OutputFile
        do! repo.DeleteThumbnail conversion.ThumbnailFile
      }

    member this.CompleteConversion(conversion: Converted, thumbnail: Thumbnail) : Task<Completed> =
      task {
        let completedConversion: Conversion.Completed =
          { Id = conversion.Id
            OutputFile = conversion.OutputFile
            ThumbnailFile = thumbnail }

        do! repo.SaveConversion(Conversion.Completed completedConversion)

        return completedConversion
      }

    member this.CompleteConversion(conversion: Thumbnailed, video: Video) : Task<Completed> =
      task {
        let completedConversion: Conversion.Completed =
          { Id = conversion.Id
            OutputFile = video
            ThumbnailFile = conversion.ThumbnailName }

        do! repo.SaveConversion(Conversion.Completed completedConversion)

        return completedConversion
      }

    member this.SaveThumbnail(conversion, thumbnail) =
      task {
        let thumbnailedConversion: Thumbnailed =
          { Id = conversion.Id
            ThumbnailName = thumbnail }

        do! repo.SaveConversion(Conversion.Thumbnailed thumbnailedConversion)

        return thumbnailedConversion
      }

    member this.SaveVideo(conversion, video) =
      task {
        let convertedConversion: Conversion.Converted =
          { Id = conversion.Id
            OutputFile = video }

        do! repo.SaveConversion(Conversion.Converted convertedConversion)

        return convertedConversion
      }

    member this.PrepareConversion(conversionId, file) =
      match file with
      | New.Link l -> repo.DownloadLink l
      | New.Document d -> repo.DownloadDocument d |> Task.map Ok
      |> TaskResult.map (fun downloadedFile ->
        { Id = conversionId
          InputFile = downloadedFile })
      |> TaskResult.taskTap (Conversion.Prepared >> repo.SaveConversion)
      |> TaskResult.taskTap repo.QueueConversion
      |> TaskResult.taskTap repo.QueueThumbnailing