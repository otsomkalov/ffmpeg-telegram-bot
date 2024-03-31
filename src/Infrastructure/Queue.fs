namespace Infrastructure

open Azure.Storage.Queues
open Domain.Workflows
open Infrastructure.Helpers
open Infrastructure.Settings
open otsom.fs.Extensions
open Domain.Repos

module Queue =
  [<CLIMutable>]
  type UploaderMessage = { ConversionId: string }

  [<RequireQualifiedAccess>]
  module Conversion =
    [<RequireQualifiedAccess>]
    module Completed =
      let queueUpload (workersSettings: WorkersSettings) : Conversion.Completed.QueueUpload =
        fun conversion ->
          let queueServiceClient = QueueServiceClient(workersSettings.ConnectionString)

          let queueClient = queueServiceClient.GetQueueClient(workersSettings.Uploader.Queue)

          let messageBody = JSON.serialize { ConversionId = conversion.Id }

          queueClient.SendMessageAsync(messageBody) |> Task.ignore

