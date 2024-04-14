namespace Domain

open System.Threading.Tasks
open Domain.Core

module Repos =
  [<RequireQualifiedAccess>]
  module Conversion =
    type Load = ConversionId -> Task<Conversion>

    [<RequireQualifiedAccess>]
    module New =
      type Save = Conversion.New -> Task<unit>

      [<RequireQualifiedAccess>]
      module InputFile =
        type DownloadLink = Conversion.New.InputLink -> Task<Result<string, Conversion.New.DownloadLinkError>>
        type DownloadDocument = Conversion.New.InputDocument -> Task<string>

    [<RequireQualifiedAccess>]
    module Prepared =
      type Save = Conversion.Prepared -> Task<unit>

      type QueueConversion = Conversion.Prepared -> Task<unit>
      type QueueThumbnailing = Conversion.Prepared -> Task<unit>

    [<RequireQualifiedAccess>]
    module Completed =
      type Save = Conversion.Completed -> Task<Conversion.Completed>

      type DeleteVideo = Conversion.Video -> Task<unit>
      type DeleteThumbnail = Conversion.Thumbnail -> Task<unit>

      type QueueUpload = Conversion.Completed -> Task<unit>

    [<RequireQualifiedAccess>]
    module Thumbnailed =
      type Save = Conversion.Thumbnailed -> Task<unit>

    [<RequireQualifiedAccess>]
    module Converted =
      type Save = Conversion.Converted -> Task<unit>