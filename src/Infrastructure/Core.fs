namespace Infrastructure

open Domain.Core

module Core =
  [<RequireQualifiedAccess>]
  module Conversion =
    [<RequireQualifiedAccess>]
    module Video =
      let value (Conversion.Video video) = video

    [<RequireQualifiedAccess>]
    module Thumbnail =
      let value (Conversion.Thumbnail thumbnail) = thumbnail