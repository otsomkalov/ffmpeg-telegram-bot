namespace Domain

open System.Threading.Tasks
open Domain.Core

module Repos =
  [<RequireQualifiedAccess>]
  module Conversion =
    [<RequireQualifiedAccess>]
    module New =
      [<RequireQualifiedAccess>]
      module InputFile =
        type DownloadLink = Conversion.New.InputLink -> Task<Result<string, Conversion.New.DownloadLinkError>>
        type DownloadDocument = Conversion.New.InputDocument -> Task<string>

    [<RequireQualifiedAccess>]
    module Prepared =
      type QueueConversion = Conversion.Prepared -> Task<unit>
      type QueueThumbnailing = Conversion.Prepared -> Task<unit>

    [<RequireQualifiedAccess>]
    module Completed =
      type DeleteVideo = Conversion.Video -> Task<unit>
      type DeleteThumbnail = Conversion.Thumbnail -> Task<unit>

      type QueueUpload = Conversion.Completed -> Task<unit>

type ILoadConversion =
  abstract LoadConversion: ConversionId -> Task<Conversion>

type ISaveConversion =
  abstract SaveConversion: Conversion -> Task<unit>

type IConversionRepo =
  inherit ILoadConversion
  inherit ISaveConversion