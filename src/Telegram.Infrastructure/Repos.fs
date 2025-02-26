namespace Telegram.Infrastructure

open Domain.Core
open MongoDB.Driver
open MongoDB.Driver.Linq
open Telegram.Core
open Telegram.Repos
open otsom.fs.Extensions
open otsom.fs.Telegram.Bot.Core

type UserConversionRepo(db: IMongoDatabase) =
  let collection = db.GetCollection<Database.Conversion>("users-conversions")

  interface IUserConversionRepo with
    member _.LoadUserConversion(ConversionId id) =
      collection.AsQueryable().FirstOrDefaultAsync(fun c -> c.Id = id)
      |> Task.map Mappings.UserConversion.fromDb

    member _.SaveUserConversion(conversion) =
      task { do! collection.InsertOneAsync(conversion |> Mappings.UserConversion.toDb) }

type UserRepo(collection: IMongoCollection<Database.User>) =
  interface IUserRepo with
    member this.LoadUser(UserId id) =
      collection.AsQueryable().FirstOrDefaultAsync(fun u -> u.Id = id)
      |> Task.map Option.ofObj
      |> TaskOption.map Mappings.User.fromDb

    member _.SaveUser user =
      task { do! collection.InsertOneAsync(user |> Mappings.User.toDb) }

type ChannelRepo(collection: IMongoCollection<Database.Channel>) =
  interface IChannelRepo with
    member _.LoadChannel(ChannelId id) =
      collection.AsQueryable().FirstOrDefaultAsync(fun c -> c.Id = id)
      |> Task.map Option.ofObj
      |> TaskOption.map Mappings.Channel.fromDb

    member _.SaveChannel channel =
      task { do! collection.InsertOneAsync(channel |> Mappings.Channel.toDb) }

type GroupRepo(collection: IMongoCollection<Database.Group>) =
  interface IGroupRepo with
    member _.LoadGroup(GroupId id) =
      collection.AsQueryable().FirstOrDefaultAsync(fun g -> g.Id = id)
      |> Task.map Option.ofObj
      |> TaskOption.map Mappings.Group.fromDb

    member _.SaveGroup group =
      task { do! collection.InsertOneAsync(group |> Mappings.Group.toDb) }