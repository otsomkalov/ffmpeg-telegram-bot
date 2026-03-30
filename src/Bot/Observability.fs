[<RequireQualifiedAccess>]
module internal Bot.Observability

open System.Diagnostics
open Infrastructure.Helpers
open OpenTelemetry.Context.Propagation

let private name = "Bot"

let ActivitySource = new ActivitySource(name)

let extractContext (context: Observability.TraceContext) =
  Propagators.DefaultTextMapPropagator.Extract(
    PropagationContext(),
    context,
    fun h k ->
      match h.TryGetValue(k) with
      | true, v -> [ v ]
      | _ -> []
  )