﻿namespace Telegram.Infrastructure

open Domain.Core
open MongoDB.Bson
open MongoDB.Driver
open MongoDB.Driver.Linq
open Telegram.Core
open Telegram.Repos
open otsom.fs.Extensions
open Infrastructure
open Telegram.Infrastructure
open Telegram.Infrastructure.Helpers

type UserConversionRepo(db: IMongoDatabase) =
  let collection = db.GetCollection<Entities.Conversion>("users-conversions")

  interface IUserConversionRepo with
    member _.LoadUserConversion(ConversionId id) =
      collection.AsQueryable().FirstOrDefaultAsync(fun c -> c.Id = ObjectId(id))
      |> Task.map _.ToUserConversion()

    member _.SaveUserConversion(conversion) =
      task { do! collection.InsertOneAsync(Entities.Conversion.FromUserConversion conversion) }

type UserRepo(collection: IMongoCollection<Entities.User>) =
  interface IUserRepo with
    member this.LoadUser(UserId id) =
      collection.AsQueryable().FirstOrDefaultAsync(fun u -> u.Id = id)
      &|> (Option.ofObj >> Option.map _.ToDomain())

    member _.SaveUser user =
      task { do! collection.InsertOneAsync(Entities.User.FromDomain user) }

type ChannelRepo(collection: IMongoCollection<Entities.Channel>) =
  interface IChannelRepo with
    member _.LoadChannel(ChannelId id) =
      collection.AsQueryable().FirstOrDefaultAsync(fun c -> c.Id = id)
      |> Task.map Option.ofObj
      |> TaskOption.map _.ToDomain()

    member _.SaveChannel channel =
      task { do! collection.InsertOneAsync(Entities.Channel.FromDomain channel) }

type GroupRepo(collection: IMongoCollection<Entities.Group>) =
  interface IGroupRepo with
    member _.LoadGroup(GroupId id) =
      collection.AsQueryable().FirstOrDefaultAsync(fun g -> g.Id = id)
      |> Task.map Option.ofObj
      |> TaskOption.map _.ToDomain()

    member _.SaveGroup group =
      task { do! collection.InsertOneAsync(Entities.Group.FromDomain group) }