namespace Infrastructure

open Domain.Core
open Microsoft.FSharp.Core
open Infrastructure.Core

module Queue =
  [<CLIMutable>]
  type BaseMessage<'a> = { OperationId: string; Data: 'a }

  [<CLIMutable>]
  type UploaderMessage = { ConversionId: string }

  [<CLIMutable>]
  type DownloaderMessage =
    { ConversionId: ConversionId
      File: Conversion.New.InputFile }

  [<CLIMutable>]
  type CleanerMessage = { ConversionId: string }

  [<RequireQualifiedAccess>]
  module Conversion =
    [<RequireQualifiedAccess>]
    module Prepared =
      [<CLIMutable>]
      type internal ConverterMessage = { Id: string; Name: string }