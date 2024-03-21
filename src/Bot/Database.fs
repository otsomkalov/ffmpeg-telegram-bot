module Bot.Database

open Domain.Core
open FSharp
open Microsoft.Extensions.Logging
open MongoDB.Driver
open otsom.fs.Extensions
open Bot.Workflows
open otsom.fs.Telegram.Bot.Core
open System
open Domain.Workflows
open Telegram.Workflows

[<RequireQualifiedAccess>]
module User =
  let load (db: IMongoDatabase) : User.Load =
    let collection = db.GetCollection "users"

    fun userId ->
      let userId' = userId |> UserId.value
      let filter = Builders<Database.User>.Filter.Eq((fun c -> c.Id), userId')

      collection.Find(filter).SingleOrDefaultAsync() |> Task.map Mappings.User.fromDb

  let save (db: IMongoDatabase) : User.Save =
    let collection = db.GetCollection "users"

    fun conversion ->
      let entity = conversion |> Mappings.User.toDb
      task { do! collection.InsertOneAsync(entity) }

  let ensureExists (db: IMongoDatabase) (loggerFactory: ILoggerFactory) : User.EnsureExists =
    let collection = db.GetCollection "users"

    fun user ->
      let userId' = user.Id |> UserId.value

      let filter = Builders<Database.User>.Filter.Eq((fun u -> u.Id), userId')

      let setOnInsert =
        [ Builders<Database.User>.Update.SetOnInsert((fun u -> u.Id), userId')
          Builders<Database.User>.Update
            .SetOnInsert((fun u -> u.Lang), (user.Lang |> Option.toObj)) ]

      collection.UpdateOneAsync(filter, Builders.Update.Combine(setOnInsert), UpdateOptions(IsUpsert = true))
      |> Task.ignore

[<RequireQualifiedAccess>]
module UserConversion =
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
        collection.ReplaceOneAsync(filter, entity) |> Task.ignore

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
        collection.ReplaceOneAsync(filter, entity) |> Task.ignore

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
        collection.ReplaceOneAsync(filter, entity) |> Task.ignore

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
    let save (db: IMongoDatabase) : Conversion.Completed.Save =
      let collection = db.GetCollection "conversions"

      fun conversion ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversion.Id)
        let entity = conversion |> Mappings.Conversion.Completed.toDb
        collection.ReplaceOneAsync(filter, entity) |> Task.ignore

[<RequireQualifiedAccess>]
module Translation =

  let private loadTranslationsMap (collection: IMongoCollection<Database.Translation>) key =
    collection.Find(fun t -> t.Lang = key).ToListAsync()
    |> Task.map (
      Seq.groupBy (_.Key)
      >> Seq.map (fun (key, translations) -> (key, translations |> Seq.map (_.Value) |> Seq.head))
      >> Map.ofSeq
    )

  let private formatWithFallback formats fallback =
    fun (key, args) ->
      match formats |> Map.tryFind key with
      | Some fmt -> String.Format(fmt, args)
      | None -> fallback

  let private loadDefaultTranslations (collection: IMongoCollection<_>) logger =
    fun () ->
      task {
        Logf.logfi logger "Loading default translations"
        let! translations = loadTranslationsMap collection Translation.DefaultLang
        Logf.logfi logger "Default translations map loaded from DB"

        let getTranslation =
          fun key -> translations |> Map.tryFind key |> Option.defaultValue key

        let formatTranslation =
          fun (key, args) -> formatWithFallback translations key (key, args)

        return (getTranslation, formatTranslation)
      }

  let getLocaleTranslations (db: IMongoDatabase) (loggerFactory: ILoggerFactory) : Translation.GetLocaleTranslations =
    let logger = loggerFactory.CreateLogger(nameof Translation.GetLocaleTranslations)
    let collection = db.GetCollection "resources"
    let getDefaultTranslations = loadDefaultTranslations collection logger

    function
    | Some l when l <> Translation.DefaultLang ->
      task {
        let! tran, tranf =  getDefaultTranslations()

        Logf.logfi logger "Loading translations for lang %s{Lang}" l

        let! localeTranslations = loadTranslationsMap collection l

        Logf.logfi logger "Translations for lang %s{Lang} is loaded" l

        let getTranslation: Translation.GetTranslation =
          fun key -> localeTranslations |> Map.tryFind key |> Option.defaultValue (tran key)

        let formatTranslation: Translation.FormatTranslation =
          fun (key, args) -> formatWithFallback localeTranslations (tranf (key, args)) (key, args)

        return (getTranslation, formatTranslation)
      }
    | _ -> getDefaultTranslations()
