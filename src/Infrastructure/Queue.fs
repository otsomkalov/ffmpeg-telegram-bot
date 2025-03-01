namespace Infrastructure

open System.Diagnostics
open Azure.Storage.Queues
open Domain.Core
open Infrastructure.Helpers
open Infrastructure.Settings
open Microsoft.FSharp.Core
open otsom.fs.Extensions
open Domain.Repos
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

  [<CLIMutable>]

  [<RequireQualifiedAccess>]
  module Conversion =

    [<RequireQualifiedAccess>]
    module New =
      let queuePreparation (workersSettings: WorkersSettings) : Conversion.New.QueuePreparation =
        fun conversionId inputFile ->
          let queueServiceClient = QueueServiceClient(workersSettings.ConnectionString)

          let queueClient =
            queueServiceClient.GetQueueClient(workersSettings.Downloader.Queue)

          let message =
            { OperationId = Activity.Current.ParentId
              Data =
                { ConversionId = conversionId
                  File = inputFile } }

          let messageBody = JSON.serialize message

          queueClient.SendMessageAsync(messageBody) |> Task.ignore

    [<RequireQualifiedAccess>]
    module Prepared =
      [<CLIMutable>]
      type internal ConverterMessage = { Id: string; Name: string }

    [<RequireQualifiedAccess>]
    module Completed =
      let queueUpload (workersSettings: WorkersSettings) operationId : Conversion.Completed.QueueUpload =
        fun conversion ->
          let queueServiceClient = QueueServiceClient(workersSettings.ConnectionString)

          let queueClient = queueServiceClient.GetQueueClient(workersSettings.Uploader.Queue)

          let messageBody =
            JSON.serialize
              { OperationId = operationId
                Data = { ConversionId = conversion.Id.Value } }

          queueClient.SendMessageAsync(messageBody) |> Task.ignore
