namespace Telegram.Infrastructure

open System.Text.RegularExpressions
open Domain.Core
open MongoDB.Bson
open Telegram.Bot.Types
open Telegram.Core
open otsom.fs.Bot
open otsom.fs.Extensions.String
open System
open Infrastructure

module Helpers =
  type Entities.Conversion with
    member this.ToUserConversion(): UserConversion =
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

  let (|FromBot|_|) (message: Message) =
    message.From
    |> Option.ofObj
    |> Option.filter (fun u -> u.IsBot)
    |> Option.map ignore