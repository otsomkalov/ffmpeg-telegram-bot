namespace Infrastructure

open Domain.Core

module Mappings =
  [<RequireQualifiedAccess>]
  module Conversion =
    [<RequireQualifiedAccess>]
    module Completed =
      let fromDb (conversion: Database.Conversion) : Conversion.Completed =
        match conversion.State with
        | Database.ConversionState.Completed ->
          { Id = conversion.Id
            OutputFile = (conversion.OutputFileName |> Video)
            ThumbnailFile = (conversion.ThumbnailFileName |> Thumbnail) }
