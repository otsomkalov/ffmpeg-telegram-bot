namespace Infrastructure

open Domain
open Domain.Core
open MongoDB.Driver
open MongoDB.Driver.Linq
open otsom.fs.Extensions

type ConversionRepo(collection: IMongoCollection<Entities.Conversion>) =
  interface IConversionRepo with
    member _.LoadConversion(ConversionId id) =
      collection.AsQueryable().FirstOrDefaultAsync(fun c -> c.Id = id)
      |> Task.map _.ToDomain

    member _.SaveConversion conversion =
      let filter = Builders<Entities.Conversion>.Filter.Eq(_.Id, conversion.Id.Value)

      collection.ReplaceOneAsync(filter, Entities.Conversion.FromDomain conversion, ReplaceOptions(IsUpsert = true))
      |> Task.ignore