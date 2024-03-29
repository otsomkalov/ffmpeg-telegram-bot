namespace Infrastructure

open System.Text.Json
open System.Text.Json.Serialization

module Helpers =
  [<RequireQualifiedAccess>]
  module JSON =
    let options =
      JsonFSharpOptions.Default().WithUnionUntagged().WithUnionUnwrapRecordCases()

    let private options' = options.ToJsonSerializerOptions()

    let serialize value =
      JsonSerializer.Serialize(value, options')
