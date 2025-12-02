namespace Telegram.Infrastructure

open Domain.Core
open MongoDB.Bson
open MongoDB.Driver
open MongoDB.Driver.Linq
open Telegram.Repos
open otsom.fs.Bot
open Infrastructure
open Telegram.Infrastructure
open Telegram.Infrastructure.Mappings
open FsToolkit.ErrorHandling

type UserConversionRepo(db: IMongoDatabase) =
  let collection = db.GetCollection<Entities.Conversion>("users-conversions")

  interface IUserConversionRepo with
    member _.LoadUserConversion(ConversionId id) =
      collection.AsQueryable().FirstOrDefaultAsync(fun c -> c.Id = ObjectId(id))
      |> Task.map _.ToUserConversion()

    member _.SaveUserConversion(conversion) =
      task { do! collection.InsertOneAsync(Entities.Conversion.FromUserConversion conversion) }

type ChatRepo(collection: IMongoCollection<Entities.Chat>) =
  interface IChatRepo with
    member this.SaveChat(chat) =
      task { do! collection.InsertOneAsync(Entities.Chat.FromDomain chat) }

    member this.LoadChat(ChatId id) =
      collection.AsQueryable().FirstOrDefaultAsync(fun g -> g.Id = id)
      |> Task.map Option.ofObj
      |> TaskOption.map _.ToDomain()