namespace Infrastructure

open System.Collections.Generic
open System.Diagnostics
open System.Text.Json
open System.Text.Json.Serialization
open Azure.Storage.Blobs
open Infrastructure.Settings
open OpenTelemetry
open OpenTelemetry.Context.Propagation

module Helpers =
  [<RequireQualifiedAccess>]
  module JSON =
    let options =
      JsonFSharpOptions.Default().WithUnionExternalTag().WithUnionUnwrapRecordCases()

    let private options' = options.ToJsonSerializerOptions()

    let serialize value =
      JsonSerializer.Serialize(value, options')


  [<RequireQualifiedAccess>]
  module Storage =
    let getBlobStream (workersSettings: WorkersSettings) =
      fun name container ->
        let blobServiceClient = BlobServiceClient(workersSettings.ConnectionString)

        let containerClient = blobServiceClient.GetBlobContainerClient(container)

        let blobClient = containerClient.GetBlobClient(name)

        blobClient.OpenWriteAsync(true)

  [<RequireQualifiedAccess>]
  module Observability =
    type TraceContext = IDictionary<string, string>

    let getTraceContext () : TraceContext =
      let activity = Activity.Current

      let propagationContext = PropagationContext(activity.Context, Baggage.Current)

      let headers = Dictionary<string, string>()

      Propagators.DefaultTextMapPropagator.Inject(propagationContext, headers, fun h k v -> h.Add(k, v))

      headers