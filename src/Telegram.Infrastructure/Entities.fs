[<RequireQualifiedAccess>]
module Telegram.Infrastructure.Entities

open MongoDB.Bson.Serialization.Attributes
open otsom.fs.Bot

[<CLIMutable>]
type Chat =
  { [<BsonId>]
    Id: int64
    Banned: bool
    Lang: string }

  member this.ToDomain() : Telegram.Chat =
    { Id = ChatId this.Id
      Banned = this.Banned
      Lang = this.Lang }

  static member FromDomain(chat: Telegram.Chat) =
    { Id = chat.Id.Value
      Banned = chat.Banned
      Lang = chat.Lang }