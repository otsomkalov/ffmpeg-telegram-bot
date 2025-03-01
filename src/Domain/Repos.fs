namespace Domain

open System.Threading.Tasks
open Domain.Core
open Domain.Core.Conversion

module Repos =
  [<RequireQualifiedAccess>]
  module Conversion =
    [<RequireQualifiedAccess>]
    module Prepared =
      type QueueConversion = Conversion.Prepared -> Task<unit>
      type QueueThumbnailing = Conversion.Prepared -> Task<unit>

    [<RequireQualifiedAccess>]
    module Completed =
      type QueueUpload = Conversion.Completed -> Task<unit>

type IDownloadLink =
  abstract DownloadLink: Conversion.New.InputLink -> Task<Result<string, Conversion.New.DownloadLinkError>>

type IDownloadDocument =
  abstract DownloadDocument: Conversion.New.InputDocument -> Task<string>

type ILoadConversion =
  abstract LoadConversion: ConversionId -> Task<Conversion>

type ISaveConversion =
  abstract SaveConversion: Conversion -> Task<unit>

type IQueueConversion =
  abstract QueueConversion: Conversion.Prepared -> Task<unit>

type IQueueThumbnailing =
  abstract QueueThumbnailing: Conversion.Prepared -> Task<unit>

type IDeleteVideo =
  abstract DeleteVideo: Video -> Task<unit>

type IDeleteThumbnail =
  abstract DeleteThumbnail: Thumbnail -> Task<unit>

type IConversionRepo =
  inherit IDownloadLink
  inherit IDownloadDocument

  inherit IQueueConversion
  inherit IQueueThumbnailing

  inherit ILoadConversion
  inherit ISaveConversion

  inherit IDeleteVideo
  inherit IDeleteThumbnail