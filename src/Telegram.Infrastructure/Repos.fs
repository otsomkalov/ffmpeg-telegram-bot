namespace Telegram.Infrastructure

open Domain.Core
open MongoDB.Driver
open Telegram.Core
open Telegram.Repos
open otsom.fs.Extensions
open otsom.fs.Telegram.Bot.Core

module Repos =
  [<RequireQualifiedAccess>]
  module UserConversion =
    let load (db: IMongoDatabase) : UserConversion.Load =
      let collection = db.GetCollection "users-conversions"

      fun conversionId ->
        let (ConversionId conversionId) = conversionId
        let filter = Builders<Database.Conversion>.Filter.Eq((fun c -> c.Id), conversionId)

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Mappings.UserConversion.fromDb

    let save (db: IMongoDatabase) : UserConversion.Save =
      let collection = db.GetCollection "users-conversions"

      fun conversion ->
        let entity = conversion |> Mappings.UserConversion.toDb
        task { do! collection.InsertOneAsync(entity) }

  [<RequireQualifiedAccess>]
  module User =
    let load (db: IMongoDatabase) : User.Load =
      let collection = db.GetCollection "users"

      fun userId ->
        let userId' = userId |> UserId.value
        let filter = Builders<Database.User>.Filter.Eq((fun c -> c.Id), userId')

        collection.Find(filter).SingleOrDefaultAsync() |> Task.map Option.ofObj |> TaskOption.map Mappings.User.fromDb

    let create (db: IMongoDatabase) : User.Create =
      let collection = db.GetCollection "users"

      fun userId lang ->
        let user : User = {Id = userId; Lang = lang}
        task {
          do! collection.InsertOneAsync(user |> Mappings.User.toDb)

          return user
        }

  [<RequireQualifiedAccess>]
  module Channel =
    let load (db: IMongoDatabase) : Channel.Load =
      let collection = db.GetCollection "channels"

      fun channelId ->
        let channelId' = channelId |> ChannelId.value
        let filter = Builders<Database.Channel>.Filter.Eq((fun c -> c.Id), channelId')

        collection.Find(filter).SingleOrDefaultAsync()
        |> Task.map Option.ofObj
        |> TaskOption.map Mappings.Channel.fromDb

    let save (db: IMongoDatabase) : Channel.Save =
      let collection = db.GetCollection "channels"

      fun channel ->
        task {
          do! collection.InsertOneAsync(channel |> Mappings.Channel.toDb)
        }
