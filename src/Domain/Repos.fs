namespace Domain

open System.Threading.Tasks
open Domain.Core

module Repos =
  [<RequireQualifiedAccess>]
  module Conversion =
    [<RequireQualifiedAccess>]
    module Completed =
      type Save = Conversion.Completed -> Task<unit>

      type Load = ConversionId -> Task<Conversion.Completed>
      type DeleteVideo = Conversion.Video -> Task<unit>
      type DeleteThumbnail = Conversion.Thumbnail -> Task<unit>

      type QueueUpload = Conversion.Completed -> Task<unit>

    [<RequireQualifiedAccess>]
    module PreparedOrConverted =
      type Load = ConversionId -> Task<Conversion.PreparedOrConverted>

    [<RequireQualifiedAccess>]
    module PreparedOrThumbnailed =
      type Load = ConversionId -> Task<Conversion.PreparedOrThumbnailed>

    [<RequireQualifiedAccess>]
    module Thumbnailed =
      type Save = Conversion.Thumbnailed -> Task<unit>

    [<RequireQualifiedAccess>]
    module Converted =
      type Save = Conversion.Converted -> Task<unit>