namespace Infrastructure

open Domain.Core
open MongoDB.Driver
open Domain.Repos
open Infrastructure.Core
open otsom.fs.Extensions

module Repos =
  [<RequireQualifiedAccess>]
  module Conversion =
    let load (collection: IMongoCollection<Entities.Conversion>) : Conversion.Load =
      fun conversionId ->
        let (ConversionId conversionId) = conversionId
        let filter = Builders<Entities.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync() |> Task.map _.ToDomain

    let save (collection: IMongoCollection<Entities.Conversion>) : Conversion.Save =
      fun conversion ->
        let filter =
          Builders<Entities.Conversion>.Filter
            .Eq((fun c -> c.Id), (conversion.Id.Value))

        collection.ReplaceOneAsync(filter, Entities.Conversion.FromDomain conversion, ReplaceOptions(IsUpsert = true))
        |> Task.ignore
