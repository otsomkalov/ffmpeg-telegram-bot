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
      type private ConverterMessage = { Id: string; Name: string }

      let queueConversion (workersSettings: WorkersSettings) operationId : Conversion.Prepared.QueueConversion =
        fun conversion ->
          let queueServiceClient = QueueServiceClient(workersSettings.ConnectionString)

          let queueClient =
            queueServiceClient.GetQueueClient(workersSettings.Converter.Input.Queue)

          { OperationId = operationId
            Data =
              { Id = conversion.Id.Value
                Name = conversion.InputFile } }
          |> JSON.serialize
          |> queueClient.SendMessageAsync
          |> Task.ignore

      let queueThumbnailing (workersSettings: WorkersSettings) operationId : Conversion.Prepared.QueueThumbnailing =
        fun conversion ->
          let queueServiceClient = QueueServiceClient(workersSettings.ConnectionString)

          let queueClient =
            queueServiceClient.GetQueueClient(workersSettings.Thumbnailer.Input.Queue)

          { OperationId = operationId
            Data =
              { Id = conversion.Id.Value
                Name = conversion.InputFile } }
          |> JSON.serialize
          |> queueClient.SendMessageAsync
          |> Task.ignore

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
