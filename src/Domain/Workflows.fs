namespace Domain

open System.Threading.Tasks
open Domain.Core
open Domain.Core.Conversion
open Microsoft.FSharp.Core
open otsom.fs.Extensions
open Domain.Repos
open shortid

module Workflows =
  [<RequireQualifiedAccess>]
  module ConversionId =
    let generate: ConversionId.Generate = fun () -> ShortId.Generate() |> ConversionId

  [<RequireQualifiedAccess>]
  module Conversion =
    let create (generateId: ConversionId.Generate) (repo: #ISaveConversion) : Create =
      fun () ->
        task {
          let newConversion: Conversion.New = { Id = generateId () }

          do! repo.SaveConversion(Conversion.New newConversion)

          return newConversion
        }

    [<RequireQualifiedAccess>]
    module New =
      let prepare
        (downloadLink: Conversion.New.InputFile.DownloadLink)
        (downloadDocument: Conversion.New.InputFile.DownloadDocument)
        (repo: #ISaveConversion)
        (queueConversion: Conversion.Prepared.QueueConversion)
        (queueThumbnailing: Conversion.Prepared.QueueThumbnailing)
        : Conversion.New.Prepare =
        fun conversionId file ->
          match file with
          | New.Link l -> downloadLink l
          | New.Document d -> downloadDocument d |> Task.map Ok
          |> TaskResult.map (fun downloadedFile ->
            { Id = conversionId
              InputFile = downloadedFile })
          |> TaskResult.taskTap (Conversion.Prepared >> repo.SaveConversion)
          |> TaskResult.taskTap queueConversion
          |> TaskResult.taskTap queueThumbnailing

    [<RequireQualifiedAccess>]
    module Prepared =
      let saveThumbnail (repo: #ISaveConversion) : Prepared.SaveThumbnail =
        fun conversion thumbnail ->
          let thumbnailedConversion: Thumbnailed =
            { Id = conversion.Id
              ThumbnailName = thumbnail }

          repo.SaveConversion(Conversion.Thumbnailed thumbnailedConversion)
          |> Task.map (fun _ -> thumbnailedConversion)

      let saveVideo (repo: #ISaveConversion) : Prepared.SaveVideo =
        fun conversion video ->
          let convertedConversion: Conversion.Converted =
            { Id = conversion.Id
              OutputFile = video }

          repo.SaveConversion(Conversion.Converted convertedConversion)
          |> Task.map (fun _ -> convertedConversion)


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
            OutputFile = (conversion.OutputFile |> Video)
            ThumbnailFile = thumbnail }

        do! repo.SaveConversion(Conversion.Completed completedConversion)

        return completedConversion
      }

    member this.CompleteConversion(conversion: Thumbnailed, video: Video) : Task<Completed> =
      task {
        let completedConversion: Conversion.Completed =
          { Id = conversion.Id
            OutputFile = video
            ThumbnailFile = conversion.ThumbnailName |> Thumbnail }

        do! repo.SaveConversion(Conversion.Completed completedConversion)

        return completedConversion
      }