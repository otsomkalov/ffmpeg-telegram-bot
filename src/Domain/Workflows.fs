namespace Domain

open Domain.Core
open Domain.Core.Conversion
open otsom.fs.Extensions
open Domain.Repos
open shortid

module Workflows =
  [<RequireQualifiedAccess>]
  module Conversion =
    let create (saveNewConversion: Conversion.New.Save) : Conversion.Create =
      fun () ->
        task {
          let newConversion: Conversion.New = { Id = ShortId.Generate() |> ConversionId }

          do! saveNewConversion newConversion

          return newConversion
        }

    [<RequireQualifiedAccess>]
    module New =
      let prepare
        (downloadLink: Conversion.New.InputFile.DownloadLink)
        (downloadDocument: Conversion.New.InputFile.DownloadDocument)
        (savePreparedConversion: Conversion.Prepared.Save)
        (queueConversion: Conversion.Prepared.QueueConversion)
        (queueThumbnailing: Conversion.Prepared.QueueThumbnailing)
        : Conversion.New.Prepare =
        fun conversionId file ->
          match file with
          | New.Link l -> downloadLink l
          | New.Document d -> downloadDocument d |> Task.map Ok
          |> TaskResult.map (fun downloadedFile ->
            { Id = conversionId
              InputFile = downloadedFile }
            : Conversion.Prepared)
          |> TaskResult.taskTap savePreparedConversion
          |> TaskResult.taskTap queueConversion
          |> TaskResult.taskTap queueThumbnailing

    [<RequireQualifiedAccess>]
    module Thumbnailed =
      let complete (saveCompletedConversion: Conversion.Completed.Save) : Conversion.Thumbnailed.Complete =
        fun conversion video ->
          saveCompletedConversion
            { Id = conversion.Id
              OutputFile = video |> Conversion.Video
              ThumbnailFile = conversion.ThumbnailName |> Conversion.Thumbnail }

    [<RequireQualifiedAccess>]
    module Prepared =
      let saveThumbnail (saveThumbnailedConversion: Conversion.Thumbnailed.Save) : Conversion.Prepared.SaveThumbnail =
        fun conversion thumbnail ->
          let thumbnailedConversion: Conversion.Thumbnailed =
            { Id = conversion.Id
              ThumbnailName = thumbnail }

          saveThumbnailedConversion thumbnailedConversion
          |> Task.map (fun _ -> thumbnailedConversion)

      let saveVideo (saveConvertedConversion: Conversion.Converted.Save) : Conversion.Prepared.SaveVideo =
        fun conversion video ->
          let convertedConversion: Conversion.Converted =
            { Id = conversion.Id
              OutputFile = video }

          saveConvertedConversion convertedConversion
          |> Task.map (fun _ -> convertedConversion)

    [<RequireQualifiedAccess>]
    module Converted =
      let complete (saveCompletedConversion: Conversion.Completed.Save) : Conversion.Converted.Complete =
        fun conversion thumbnail ->
          saveCompletedConversion
            { Id = conversion.Id
              OutputFile = (conversion.OutputFile |> Conversion.Video)
              ThumbnailFile = (thumbnail |> Conversion.Thumbnail) }
