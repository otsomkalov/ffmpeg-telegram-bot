namespace Domain

open System.Threading.Tasks
open Domain.Core
open Domain.Core.Conversion

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
      type QueueUpload = Conversion.Completed -> Task<unit>

type ILoadConversion =
  abstract LoadConversion: ConversionId -> Task<Conversion>

type ISaveConversion =
  abstract SaveConversion: Conversion -> Task<unit>

type IDeleteVideo =
  abstract DeleteVideo: Video -> Task<unit>

type IDeleteThumbnail =
  abstract DeleteThumbnail: Thumbnail -> Task<unit>

type IConversionRepo =
  inherit ILoadConversion
  inherit ISaveConversion

  inherit IDeleteVideo
  inherit IDeleteThumbnail