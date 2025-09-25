module Telegram.Infrastructure.Mappings

open Domain.Core
open Infrastructure
open MongoDB.Bson
open Telegram.Core
open otsom.fs.Bot

type Entities.Conversion with
  member this.ToUserConversion() : UserConversion =
    { ConversionId = (this.Id |> string |> ConversionId)
      ReceivedMessageId = (this.ReceivedMessageId |> ChatMessageId)
      SentMessageId = BotMessageId this.SentMessageId
      ChatId = ChatId this.ChatId }

  static member FromUserConversion(conversion: UserConversion) : Entities.Conversion =
    Entities.Conversion(
      Id = ObjectId(conversion.ConversionId.Value),
      ReceivedMessageId = conversion.ReceivedMessageId.Value,
      SentMessageId = conversion.SentMessageId.Value,
      ChatId = conversion.ChatId.Value
    )