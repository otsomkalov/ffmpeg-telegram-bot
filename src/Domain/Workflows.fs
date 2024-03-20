namespace Domain

open System.Threading.Tasks
open Domain.Core

module Workflows =

  [<RequireQualifiedAccess>]
  module Conversion =
    [<RequireQualifiedAccess>]
    module Completed =
      type Load = ConversionId -> Task<Conversion.Completed>
      type DeleteVideo = string -> Task<unit>
      type DeleteThumbnail = string -> Task<unit>
