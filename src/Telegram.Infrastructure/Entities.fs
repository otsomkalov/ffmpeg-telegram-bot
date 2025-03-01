[<RequireQualifiedAccess>]
module Telegram.Infrastructure.Entities

open Infrastructure
open MongoDB.Bson
open MongoDB.Bson.Serialization.Attributes
open Telegram.Core
open otsom.fs.Telegram.Bot

[<CLIMutable>]
type Channel = {
    Id: int64
    Banned: bool
  }
with
  member this.ToDomain(): Telegram.Core.Channel =
    { Id = ChannelId(this.Id)
      Banned = this.Banned }

  static member FromDomain(channel: Telegram.Core.Channel) =
    {Id = channel.Id.Value; Banned = channel.Banned}

[<CLIMutable>]
type Group =
  {
    [<BsonId>]
    Id: int64
    Banned: bool
  }
  member this.ToDomain(): Telegram.Core.Group =
    { Id = GroupId(this.Id)
      Banned = this.Banned }

  static member FromDomain(group: Telegram.Core.Group) =
    {Id = group.Id.Value; Banned = group.Banned}

[<CLIMutable>]
type User ={
  [<BsonId>]
  Id: int64
  Banned: bool
  Lang: string | null
}
with
  member this.ToDomain(): Telegram.Core.User =
    { Id = Core.UserId(this.Id)
      Banned = this.Banned
      Lang = this.Lang |> Option.ofObj }

  static member FromDomain(user: Telegram.Core.User) =
    { Id = user.Id.Value; Banned = user.Banned; Lang = user.Lang |> Option.toObj }

type Translation() =
  [<BsonId;BsonElement>]
  member val Id: ObjectId = ObjectId() with get, set

  member val Key: string = "" with get, set
  member val Value: string = "" with get, set
  member val Lang: string = "" with get, set

  member this.ToDomain(): Telegram.Core.Translation =
    { Key = this.Key
      Value = this.Value }