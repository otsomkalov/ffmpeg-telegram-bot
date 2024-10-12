namespace Telegram.Infrastructure

open Domain.Core
open Telegram.Core
open otsom.fs.Telegram.Bot.Core
open Infrastructure.Core
open Telegram.Infrastructure.Core

module Mappings =

  [<RequireQualifiedAccess>]
  module UserConversion =
    let fromDb (conversion: Database.Conversion) : UserConversion =
      { ConversionId = (conversion.Id |> ConversionId)
        UserId = (conversion.UserId |> Option.ofNullable |> Option.map UserId)
        ReceivedMessageId = (conversion.ReceivedMessageId |> UserMessageId)
        SentMessageId = BotMessageId conversion.SentMessageId
        ChatId = UserId conversion.ChatId }

    let toDb (conversion: UserConversion) : Database.Conversion =
      Database.Conversion(
        Id = (conversion.ConversionId |> ConversionId.value),
        UserId = (conversion.UserId |> Option.map UserId.value |> Option.toNullable),
        ReceivedMessageId = (conversion.ReceivedMessageId |> UserMessageId.value),
        SentMessageId = (conversion.SentMessageId |> BotMessageId.value),
        ChatId = (conversion.ChatId |> UserId.value)
      )

  [<RequireQualifiedAccess>]
  module User =
    let fromDb (user: Database.User) : User =
      { Id = UserId user.Id
        Lang = (user.Lang |> Option.ofObj)
        Banned = user.Banned }

    let toDb (user: User) : Database.User =
      Database.User(Id = (user.Id |> UserId.value), Lang = (user.Lang |> Option.toObj), Banned = user.Banned)

  [<RequireQualifiedAccess>]
  module Group =
    let fromDb (group: Database.Group) : Group =
      { Id = GroupId group.Id; Banned = group.Banned }

    let toDb (group: Group) : Database.Group =
      Database.Group(Id = (group.Id |> GroupId.value), Banned = group.Banned)

  [<RequireQualifiedAccess>]
  module Channel =
    let fromDb (channel: Database.Channel) : Channel =
      { Id = ChannelId channel.Id; Banned = channel.Banned }

    let toDb (channel: Channel) : Database.Channel =
      Database.Channel(Id = (channel.Id |> ChannelId.value), Banned = channel.Banned)
