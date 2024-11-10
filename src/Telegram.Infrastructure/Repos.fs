namespace Telegram.Infrastructure

open Domain.Core
open MongoDB.Driver
open MongoDB.Driver.Linq
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
    let load (collection: IMongoCollection<Database.User>) : User.Load =
      fun userId ->
        let userId' = userId |> UserId.value

        collection.AsQueryable().SingleOrDefaultAsync(fun u -> u.Id = userId')
        |> Task.map Option.ofObj
        |> TaskOption.map Mappings.User.fromDb

    let create (collection: IMongoCollection<Database.User>) : User.Create =
      fun user -> task { do! collection.InsertOneAsync(user |> Mappings.User.toDb) }

  [<RequireQualifiedAccess>]
  module Channel =
    let load (collection: IMongoCollection<Database.Channel>) : Channel.Load =
      fun channelId ->
        let channelId' = channelId |> ChannelId.value

        collection.AsQueryable().SingleOrDefaultAsync(fun c -> c.Id = channelId')
        |> Task.map Option.ofObj
        |> TaskOption.map Mappings.Channel.fromDb

    let save (collection: IMongoCollection<Database.Channel>) : Channel.Save =
      fun channel -> task { do! collection.InsertOneAsync(channel |> Mappings.Channel.toDb) }

  [<RequireQualifiedAccess>]
  module Group =
    let load (collection: IMongoCollection<Database.Group>) : Group.Load =
      fun groupId ->
        let groupId' = groupId |> GroupId.value

        collection.AsQueryable().SingleOrDefaultAsync(fun g -> g.Id = groupId')
        |> Task.map Option.ofObj
        |> TaskOption.map Mappings.Group.fromDb

    let save (collection: IMongoCollection<Database.Group>) : Group.Save =
      fun group -> task { do! collection.InsertOneAsync(group |> Mappings.Group.toDb) }
