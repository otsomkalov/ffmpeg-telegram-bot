namespace Domain

open System.Threading.Tasks
open Microsoft.FSharp.Core

module Core =
  type ConversionId =
    | ConversionId of string
    member this.Value = let (ConversionId id) = this in id

  module ConversionId =
    type Generate = unit -> ConversionId

  module Conversion =
    type New = { Id: ConversionId }

    type Create = unit -> Task<New>

    type Prepared = { Id: ConversionId; InputFile: string }

    type Converted =
      { Id: ConversionId; OutputFile: string }

    type Thumbnailed =
      { Id: ConversionId
        ThumbnailName: string }

    type Video = Video of string
    type Thumbnail = Thumbnail of string

    type Completed =
      { Id: ConversionId
        OutputFile: Video
        ThumbnailFile: Thumbnail }

    [<RequireQualifiedAccess>]
    module New =
      type InputLink = { Url: string }
      type InputDocument = { Id: string; Name: string }

      type InputFile =
        | Link of InputLink
        | Document of InputDocument

      [<RequireQualifiedAccess>]
      type DownloadLinkError =
        | Unauthorized
        | NotFound
        | ServerError

      type Prepare = ConversionId -> InputFile -> Task<Result<Prepared, DownloadLinkError>>

      type QueuePreparation = ConversionId -> InputFile -> Task<unit>

    [<RequireQualifiedAccess>]
    module Prepared =
      type SaveThumbnail = Prepared -> string -> Task<Thumbnailed>
      type SaveVideo = Prepared -> string -> Task<Converted>

  type Conversion =
    | New of Conversion.New
    | Prepared of Conversion.Prepared
    | Converted of Conversion.Converted
    | Thumbnailed of Conversion.Thumbnailed
    | Completed of Conversion.Completed

    member this.Id =
      match this with
      | New { Id = id }
      | Prepared { Id = id }
      | Converted { Id = id }
      | Thumbnailed { Id = id }
      | Completed { Id = id } -> id

open Core.Conversion

type ICompleteConversion =
  abstract CompleteConversion: Converted * Thumbnail -> Task<Completed>
  abstract CompleteConversion: Thumbnailed * Video -> Task<Completed>

type ICleanupConversion =
  abstract CleanupConversion: Completed -> Task<unit>

type IConversionService =
  inherit ICleanupConversion

  inherit ICompleteConversion