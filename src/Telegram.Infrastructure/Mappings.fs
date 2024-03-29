namespace Telegram.Infrastructure

open Telegram.Core
open otsom.fs.Telegram.Bot.Core

module Mappings =

  [<RequireQualifiedAccess>]
  module UserConversion =
    let fromDb (conversion: Database.Conversion) : UserConversion =
        { ConversionId = conversion.Id
          UserId = (conversion.UserId |> Option.ofNullable |> Option.map UserId)
          ReceivedMessageId = (conversion.ReceivedMessageId |> UserMessageId)
          SentMessageId = BotMessageId conversion.SentMessageId
          ChatId = UserId conversion.ChatId }
