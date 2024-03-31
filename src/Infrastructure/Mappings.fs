namespace Infrastructure

open Domain.Core
open Infrastructure.Core

module Mappings =
  [<RequireQualifiedAccess>]
  module Conversion =
    [<RequireQualifiedAccess>]
    module Completed =
      let fromDb (conversion: Database.Conversion) : Conversion.Completed =
        match conversion.State with
        | Database.ConversionState.Completed ->
          { Id = conversion.Id
            OutputFile = (conversion.OutputFileName |> Conversion.Video)
            ThumbnailFile = (conversion.ThumbnailFileName |> Conversion.Thumbnail) }

      let toDb (conversion: Conversion.Completed) : Database.Conversion =
        Database.Conversion(
          Id = conversion.Id,
          OutputFileName = (conversion.OutputFile |> Conversion.Video.value),
          ThumbnailFileName = (conversion.ThumbnailFile |> Conversion.Thumbnail.value),
          State = Database.ConversionState.Completed
        )
