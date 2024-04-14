namespace Infrastructure

open Domain.Core
open MongoDB.Driver
open Domain.Repos
open Infrastructure.Mappings
open Infrastructure.Core
open otsom.fs.Extensions

module Repos =
  [<RequireQualifiedAccess>]
  module Conversion =
    let load (db: IMongoDatabase) : Conversion.Load =
      let collection = db.GetCollection "conversions"

      fun conversionId ->
        let (ConversionId conversionId) = conversionId
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Conversion.fromDb

    let save (db: IMongoDatabase) : Conversion.Save =
      let collection = db.GetCollection "conversions"

      fun conversion ->
        let filter =
          Builders<Database.Conversion>.Filter
            .Eq((fun c -> c.Id), (conversion.Id |> ConversionId.value))

        let entity =
          match conversion with
          | Conversion.New conversion -> conversion |> Mappings.Conversion.New.toDb
          | Conversion.Prepared conversion -> conversion |> Mappings.Conversion.Prepared.toDb
          | Conversion.Converted conversion -> conversion |> Mappings.Conversion.Converted.toDb
          | Conversion.Thumbnailed conversion -> conversion |> Mappings.Conversion.Thumbnailed.toDb
          | Conversion.Completed conversion -> conversion |> Mappings.Conversion.Completed.toDb

        collection.ReplaceOneAsync(filter, entity, ReplaceOptions(IsUpsert = true)) |> Task.ignore
