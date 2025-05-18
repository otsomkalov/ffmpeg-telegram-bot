namespace Domain

open System.Threading.Tasks
open Domain.Core
open Domain.Core.Conversion

type IDownloadLink =
  abstract DownloadLink: Conversion.New.InputLink -> Task<Result<string, Conversion.New.DownloadLinkError>>

type IDownloadDocument =
  abstract DownloadDocument: Conversion.New.InputDocument -> Task<string>

type ILoadConversion =
  abstract LoadConversion: ConversionId -> Task<Conversion>

type ISaveConversion =
  abstract SaveConversion: Conversion -> Task<unit>

type IQueuePreparation =
  abstract QueuePreparation: ConversionId * New.InputFile -> Task<unit>

type IQueueConversion =
  abstract QueueConversion: Conversion.Prepared -> Task<unit>

type IQueueThumbnailing =
  abstract QueueThumbnailing: Conversion.Prepared -> Task<unit>

type IDeleteVideo =
  abstract DeleteVideo: Video -> Task<unit>

type IDeleteThumbnail =
  abstract DeleteThumbnail: Thumbnail -> Task<unit>

type IGenerateConversionId =
  abstract GenerateConversionId: unit -> ConversionId

type IQueueUpload =
  abstract QueueUpload: Completed -> Task<unit>

type IConversionRepo =
  inherit IGenerateConversionId

  inherit IDownloadLink
  inherit IDownloadDocument

  inherit IQueuePreparation
  inherit IQueueConversion
  inherit IQueueThumbnailing

  inherit ILoadConversion
  inherit ISaveConversion

  inherit IDeleteVideo
  inherit IDeleteThumbnail

  inherit IQueueUpload