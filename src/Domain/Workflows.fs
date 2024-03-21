namespace Domain

open Domain.Core
open otsom.fs.Extensions
open Domain.Deps

module Workflows =

  [<RequireQualifiedAccess>]
  module Conversion =

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
          let completedConversion: Conversion.Completed =
            { Id = conversion.Id
              OutputFile = conversion.OutputFile
              ThumbnailFile = thumbnail }

          saveCompletedConversion completedConversion
          |> Task.map (fun _ -> completedConversion)

    [<RequireQualifiedAccess>]
    module Thumbnailed =
      let complete (saveCompletedConversion: Conversion.Completed.Save) : Conversion.Thumbnailed.Complete =
        fun conversion video ->
          let completedConversion: Conversion.Completed =
            { Id = conversion.Id
              OutputFile = video
              ThumbnailFile = conversion.ThumbnailName }

          saveCompletedConversion completedConversion
          |> Task.map (fun _ -> completedConversion)
