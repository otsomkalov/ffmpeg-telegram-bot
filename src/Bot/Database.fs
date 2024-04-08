module Bot.Database

open Domain.Core
open Microsoft.Extensions.Logging
open MongoDB.Driver
open otsom.fs.Extensions
open otsom.fs.Telegram.Bot.Core
open Domain.Workflows
open Telegram.Workflows
open Domain.Repos
open Infrastructure.Mappings
open Infrastructure.Core
open Bot.Workflows

[<RequireQualifiedAccess>]
module User =
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
        |> Task.map Conversion.Prepared.fromDb

  [<RequireQualifiedAccess>]
  module Converted =
    let load (db: IMongoDatabase) =
      let collection = db.GetCollection "conversions"

      fun conversionId ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Conversion.Converted.fromDb

  [<RequireQualifiedAccess>]
  module Thumbnailed =
    let load (db: IMongoDatabase) =
      let collection = db.GetCollection "conversions"

      fun conversionId ->
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Conversion.Thumbnailed.fromDb