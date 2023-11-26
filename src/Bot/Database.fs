[<RequireQualifiedAccess>]
module Bot.Database

open System.Threading.Tasks
open MongoDB.Driver
open otsom.FSharp.Extensions

let loadNewConversion (db: IMongoDatabase) : string -> Task<Domain.NewConversion> =
  let collection = db.GetCollection "conversions"

  fun conversionId ->
    let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

    collection.Find(filter).SingleOrDefaultAsync()
    |> Task.map Mappings.NewConversion.fromDb

let saveNewConversion (db: IMongoDatabase) =
  let collection = db.GetCollection "conversions"

  fun conversion ->
    let entity = conversion |> Mappings.NewConversion.toDb
    task { do! collection.InsertOneAsync(entity) }

let saveConversion (db: IMongoDatabase) : Domain.Conversion -> Task<unit> =
  let collection = db.GetCollection "conversions"

  fun conversion ->
    let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversion.Id)

    let entity = conversion |> Mappings.Conversion.toDb
    collection.ReplaceOneAsync(filter, entity) |> Task.map ignore

let loadConversion (db: IMongoDatabase) : string -> Task<Domain.Conversion> =
  let collection = db.GetCollection "conversions"

  fun conversionId ->
    let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

    collection.Find(filter).SingleOrDefaultAsync()
    |> Task.map Mappings.Conversion.fromDb