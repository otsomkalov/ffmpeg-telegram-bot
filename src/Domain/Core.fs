namespace Domain


module Core =
  type ConversionId = ConversionId of string

  type Video = Video of string
  type Thumbnail = Thumbnail of string

  [<RequireQualifiedAccess>]
  module Conversion =
    type Completed =
      { Id: string
        OutputFile: Video
        ThumbnailFile: Thumbnail }

  [<RequireQualifiedAccess>]
  module Video =
    let value (Video video) = video

  [<RequireQualifiedAccess>]
  module Thumbnail =
    let value (Thumbnail thumbnail) = thumbnail