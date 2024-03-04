module Bot.Database

open System.Threading.Tasks
open Google.Protobuf.WellKnownTypes
open MongoDB.Driver
open otsom.FSharp.Extensions
open Bot.Workflows

[<RequireQualifiedAccess>]
module User =
  let load (db: IMongoDatabase) : User.Load =
    let collection = db.GetCollection "users"

    fun userId ->
      let filter = Builders<Database.User>.Filter.Eq((fun c -> c.Id), userId)

      collection.Find(filter).SingleOrDefaultAsync()
      |> Task.map Mappings.User.fromDb

  let save (db: IMongoDatabase) : User.Save =
    let collection = db.GetCollection "users"

    fun conversion ->
      let entity = conversion |> Mappings.User.toDb
      task { do! collection.InsertOneAsync(entity) }

  let ensureExists (db: IMongoDatabase) : User.EnsureExists =
    let collection = db.GetCollection "users"

    fun user ->
      let filter = Builders<Database.User>.Filter.Eq((fun u -> u.Id), user.Id)
      let entity = user |> Mappings.User.toDb
      let setOnInsert =
        [Builders<Database.User>.Update.SetOnInsert((fun u -> u.Id), user.Id)
         Builders<Database.User>.Update.SetOnInsert((fun u -> u.Lang), user.Lang)]

      task { do! (collection.UpdateOneAsync(filter, Builders.Update.Combine(setOnInsert), UpdateOptions(IsUpsert = true)) |> Task.map ignore) }

[<RequireQualifiedAccess>]
module UserConversion =
  let load (db: IMongoDatabase) : UserConversion.Load =
    let collection = db.GetCollection "users-conversions"

    fun conversionId ->
      let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

      collection.Find(filter).SingleOrDefaultAsync()
      |> Task.map Mappings.UserConversion.fromDb

  let save (db: IMongoDatabase) : UserConversion.Save =
    let collection = db.GetCollection "users-conversions"

    fun conversion ->
      let entity = conversion |> Mappings.UserConversion.toDb
      task { do! collection.InsertOneAsync(entity) }

[<RequireQualifiedAccess>]
module Conversion =
  [<RequireQualifiedAccess>]
  module New =
    let load (db: IMongoDatabase) : Conversion.New.Load =
      let collection = db.GetCollection "conversions"

      fun conversionId ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Mappings.Conversion.New.fromDb

    let save (db: IMongoDatabase) : Conversion.New.Save =
      let collection = db.GetCollection "conversions"

      fun conversion ->
        let entity = conversion |> Mappings.Conversion.New.toDb
        task { do! collection.InsertOneAsync(entity) }

  [<RequireQualifiedAccess>]
  module Prepared =
    let load (db: IMongoDatabase) : Conversion.Prepared.Load =
      let collection = db.GetCollection "conversions"

      fun conversionId ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Mappings.Conversion.Prepared.fromDb

    let save (db: IMongoDatabase) : Conversion.Prepared.Save =
      let collection = db.GetCollection "conversions"

      fun conversion ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversion.Id)
        let entity = conversion |> Mappings.Conversion.Prepared.toDb
        collection.ReplaceOneAsync(filter, entity) |> Task.map ignore

  [<RequireQualifiedAccess>]
  module Converted =
    let load (db: IMongoDatabase) =
      let collection = db.GetCollection "conversions"

      fun conversionId ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Mappings.Conversion.Converted.fromDb

    let save (db: IMongoDatabase) : Conversion.Converted.Save =
      let collection = db.GetCollection "conversions"

      fun conversion ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversion.Id)
        let entity = conversion |> Mappings.Conversion.Converted.toDb
        collection.ReplaceOneAsync(filter, entity) |> Task.map ignore

  [<RequireQualifiedAccess>]
  module Thumbnailed =
    let load (db: IMongoDatabase) =
      let collection = db.GetCollection "conversions"

      fun conversionId ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Mappings.Conversion.Thumbnailed.fromDb

    let save (db: IMongoDatabase) : Conversion.Thumbnailed.Save =
      let collection = db.GetCollection "conversions"

      fun conversion ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversion.Id)
        let entity = conversion |> Mappings.Conversion.Thumbnailed.toDb
        collection.ReplaceOneAsync(filter, entity) |> Task.map ignore

  [<RequireQualifiedAccess>]
  module PreparedOrConverted =
    let load (db: IMongoDatabase) : Conversion.PreparedOrConverted.Load =
      let collection = db.GetCollection "conversions"

      fun conversionId ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Mappings.Conversion.PreparedOrConverted.fromDb

  [<RequireQualifiedAccess>]
  module PreparedOrThumbnailed =
    let load (db: IMongoDatabase) : Conversion.PreparedOrThumbnailed.Load =
      let collection = db.GetCollection "conversions"

      fun conversionId ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Mappings.Conversion.PreparedOrThumbnailed.fromDb

  [<RequireQualifiedAccess>]
  module Completed =
    let load (db: IMongoDatabase) : Conversion.Completed.Load =
      let collection = db.GetCollection "conversions"

      fun conversionId ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Mappings.Conversion.Completed.fromDb

    let save (db: IMongoDatabase) : Conversion.Completed.Save =
      let collection = db.GetCollection "conversions"

      fun conversion ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversion.Id)
        let entity = conversion |> Mappings.Conversion.Completed.toDb
        collection.ReplaceOneAsync(filter, entity) |> Task.map ignore

[<RequireQualifiedAccess>]
module Translation =
  let getLocaleTranslations (db: IMongoDatabase) : Translation.GetLocaleTranslations =
    fun lang ->
      let collection = db.GetCollection "resources"

      let localeTranslations =
        let filter = Builders<Database.Translation>.Filter.Eq((fun t -> t.Lang), lang)

        collection.Find(filter).ToList()
        |> (Seq.groupBy(_.Key) >> Seq.map(fun (key, translations) -> (key, translations |> Seq.map (_.Value) |> Seq.head)) >> Map.ofSeq)

      let defaultTranslations =
        let filter = Builders<Database.Translation>.Filter.Eq((fun t -> t.Lang), Translation.DefaultLang)

        collection.Find(filter).ToList()
        |> (Seq.groupBy(_.Key) >> Seq.map(fun (key, translations) -> (key, translations |> Seq.map (_.Value) |> Seq.head)) >> Map.ofSeq)


      fun key ->
        let localeTranslation =
          localeTranslations
          |> Map.tryFind key

        let fallbackedTranslation =
          match localeTranslation with
          | Some t -> Some t
          | None -> defaultTranslations |> Map.tryFind key

        fallbackedTranslation |> Option.defaultValue key