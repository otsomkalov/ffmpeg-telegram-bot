[<RequireQualifiedAccess>]
module Bot.Mappings

open Bot.Workflows
open Telegram.Core
open otsom.fs.Telegram.Bot.Core
open Telegram.Infrastructure.Core
open Domain.Core
open Domain.Repos
open Infrastructure.Core

[<RequireQualifiedAccess>]
module User =
  let toDb (user: User) : Database.User =
    Database.User(Id = (user.Id |> UserId.value), Lang = (user.Lang |> Option.toObj))

  let fromTg (user: Telegram.Bot.Types.User) : User =
    { Id = UserId user.Id
      Lang = Option.ofObj user.LanguageCode }

[<RequireQualifiedAccess>]
module UserConversion =
  let toDb (conversion: UserConversion) : Database.Conversion =
    Database.Conversion(
      Id = conversion.ConversionId,
      UserId = (conversion.UserId |> Option.map UserId.value |> Option.toNullable),
      ReceivedMessageId = (conversion.ReceivedMessageId |> UserMessageId.value),
      SentMessageId = (conversion.SentMessageId |> BotMessageId.value),
      ChatId = (conversion.ChatId |> UserId.value)
    )

[<RequireQualifiedAccess>]
module Conversion =
  [<RequireQualifiedAccess>]
  module New =
    let fromDb (conversion: Database.Conversion) : Domain.Conversion.New =
      match conversion.State with
      | Database.ConversionState.New -> { Id = conversion.Id }

    let toDb (conversion: Domain.Conversion.New) : Database.Conversion =
      Database.Conversion(Id = conversion.Id, State = Database.ConversionState.New)

[<RequireQualifiedAccess>]
module Translation =
  let fromDb (translation: Database.Translation) : Translation =
    { Key = translation.Key
      Value = translation.Value }
