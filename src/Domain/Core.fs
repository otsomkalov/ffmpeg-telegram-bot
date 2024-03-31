namespace Domain

open System.Threading.Tasks
open otsom.fs.Telegram.Bot.Core


module Core =
  type User = { Id: UserId; Lang: string option }

  type ConversionId = ConversionId of string

  [<RequireQualifiedAccess>]
  module Conversion =
    type Prepared = { Id: string; InputFile: string }
    type Converted = { Id: string; OutputFile: string }
    type Thumbnailed = { Id: string; ThumbnailName: string }

    type PreparedOrConverted = Choice<Prepared, Converted>

    type Video = Video of string
    type Thumbnail = Thumbnail of string

    type Completed =
      { Id: string
        OutputFile: Video
        ThumbnailFile: Thumbnail }

    [<RequireQualifiedAccess>]
    module Prepared =
      type SaveThumbnail = Prepared -> string -> Task<Thumbnailed>

    [<RequireQualifiedAccess>]
    module Converted =
      type Complete = Converted -> string -> Task<Completed>