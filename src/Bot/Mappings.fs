[<RequireQualifiedAccess>]
module Bot.Mappings

open Bot.Workflows
open Telegram.Core
open otsom.fs.Telegram.Bot.Core
open Telegram.Infrastructure.Core
open Domain.Core
open Domain.Deps
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
  module Prepared =
    let fromDb (conversion: Database.Conversion) : Conversion.Prepared =
      match conversion.State with
      | Database.ConversionState.Prepared ->
        { Id = conversion.Id
          InputFile = conversion.InputFileName }

    let toDb (conversion: Conversion.Prepared) : Database.Conversion =
      Database.Conversion(Id = conversion.Id, InputFileName = conversion.InputFile, State = Database.ConversionState.Prepared)

  [<RequireQualifiedAccess>]
  module Converted =
    let fromDb (conversion: Database.Conversion) : Conversion.Converted =
      match conversion.State with
      | Database.ConversionState.Converted ->
        { Id = conversion.Id
          OutputFile = conversion.OutputFileName }

    let toDb (conversion: Conversion.Converted) : Database.Conversion =
      Database.Conversion(Id = conversion.Id, OutputFileName = conversion.OutputFile, State = Database.ConversionState.Converted)

  [<RequireQualifiedAccess>]
  module Thumbnailed =
    let fromDb (conversion: Database.Conversion) : Conversion.Thumbnailed =
      match conversion.State with
      | Database.ConversionState.Thumbnailed ->
        { Id = conversion.Id
          ThumbnailName = conversion.ThumbnailFileName }

    let toDb (conversion: Conversion.Thumbnailed) : Database.Conversion =
      Database.Conversion(Id = conversion.Id, ThumbnailFileName = conversion.ThumbnailName, State = Database.ConversionState.Thumbnailed)

  [<RequireQualifiedAccess>]
  module PreparedOrConverted =
    let fromDb (conversion: Database.Conversion) : Conversion.PreparedOrConverted =
      match conversion.State with
      | Database.ConversionState.Prepared -> Prepared.fromDb conversion |> Choice1Of2
      | Database.ConversionState.Converted -> Converted.fromDb conversion |> Choice2Of2

  [<RequireQualifiedAccess>]
  module PreparedOrThumbnailed =
    let fromDb (conversion: Database.Conversion) : Conversion.PreparedOrThumbnailed =
      match conversion.State with
      | Database.ConversionState.Prepared -> Prepared.fromDb conversion |> Choice1Of2
      | Database.ConversionState.Thumbnailed -> Thumbnailed.fromDb conversion |> Choice2Of2

  [<RequireQualifiedAccess>]
  module Completed =
    let toDb (conversion: Conversion.Completed) : Database.Conversion =
      Database.Conversion(
        Id = conversion.Id,
        OutputFileName = (conversion.OutputFile |> Conversion.Video.value),
        ThumbnailFileName = (conversion.ThumbnailFile |> Conversion.Thumbnail.value),
        State = Database.ConversionState.Completed
      )

[<RequireQualifiedAccess>]
module Translation =
  let fromDb (translation: Database.Translation) : Translation =
    { Key = translation.Key
      Value = translation.Value }
