[<RequireQualifiedAccess>]
module internal Bot.Observability

open System.Diagnostics
open System.Reflection
open Infrastructure.Helpers
open OpenTelemetry.Context.Propagation

let private assembly = Assembly.GetExecutingAssembly()

let ActivitySource = new ActivitySource(assembly.FullName)

let extractContext (context: Observability.TraceContext) =
  Propagators.DefaultTextMapPropagator.Extract(
    PropagationContext(),
    context,
    fun h k ->
      match h.TryGetValue(k) with
      | true, v -> [ v ]
      | _ -> []
  )