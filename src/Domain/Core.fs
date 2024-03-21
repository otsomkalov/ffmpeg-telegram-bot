namespace Domain


module Core =
  type ConversionId = ConversionId of string

  [<RequireQualifiedAccess>]
  module Conversion =
    type Completed =
      { Id: string
        OutputFile: string
        ThumbnailFile: string }