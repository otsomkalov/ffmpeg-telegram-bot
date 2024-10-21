namespace Telegram.Infrastructure

open Domain.Core
open FSharp
open Microsoft.Extensions.Logging
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
    let load (db: IMongoDatabase) (loggerFactory: ILoggerFactory) : User.Load =
      let logger = loggerFactory.CreateLogger(nameof User.Load)
      let collection = db.GetCollection "users"

      fun userId ->
        let userId' = userId |> UserId.value
        let filter = Builders<Database.User>.Filter.Eq((fun c -> c.Id), userId')

        Logf.logfi logger "Loading user %i{UserId} from DB" userId'

        task {
          let! entity = collection.Find(filter).SingleOrDefaultAsync()

          Logf.logfi logger "User %i{UserId} is loaded from DB" userId'

          return entity |> Option.ofObj |> Option.map Mappings.User.fromDb
        }

    let create (db: IMongoDatabase) : User.Create =
      let collection = db.GetCollection "users"

      fun user -> task { do! collection.InsertOneAsync(user |> Mappings.User.toDb) }

  [<RequireQualifiedAccess>]
  module Channel =
    let load (db: IMongoDatabase) (loggerFactory: ILoggerFactory) : Channel.Load =
      let logger = loggerFactory.CreateLogger(nameof Channel.Load)
      let collection = db.GetCollection "channels"

      fun channelId ->
        let channelId' = channelId |> ChannelId.value
        let filter = Builders<Database.Channel>.Filter.Eq((fun c -> c.Id), channelId')

        Logf.logfi logger "Loading channel %i{ChannelId} from DB" channelId'

        task {
          let! entity = collection.Find(filter).SingleOrDefaultAsync()

          Logf.logfi logger "Channel %i{ChannelId} is loaded from DB" channelId'

          return
            entity
            |> Option.ofObj
            |> Option.map Mappings.Channel.fromDb
        }

    let save (db: IMongoDatabase) : Channel.Save =
      let collection = db.GetCollection "channels"

      fun channel -> task { do! collection.InsertOneAsync(channel |> Mappings.Channel.toDb) }

  [<RequireQualifiedAccess>]
  module Group =
    let load (db: IMongoDatabase) (loggerFactory: ILoggerFactory) : Group.Load =
      let logger = loggerFactory.CreateLogger(nameof Group.Load)
      let collection = db.GetCollection "groups"

      fun groupId ->
        let groupId' = groupId |> GroupId.value
        let filter = Builders<Database.Group>.Filter.Eq((fun c -> c.Id), groupId')

        Logf.logfi logger "Loading group %i{GroupId} from DB" groupId'

        task {
          let! entity = collection.Find(filter).SingleOrDefaultAsync()

          Logf.logfi logger "Group %i{GroupId} is loaded from DB" groupId'

          return
            entity
            |> Option.ofObj
            |> Option.map Mappings.Group.fromDb
        }

    let save (db: IMongoDatabase) : Group.Save =
      let collection = db.GetCollection "groups"

      fun group -> task { do! collection.InsertOneAsync(group |> Mappings.Group.toDb) }
