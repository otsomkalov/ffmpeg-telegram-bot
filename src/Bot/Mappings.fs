[<RequireQualifiedAccess>]
module Bot.Mappings

[<RequireQualifiedAccess>]
module User =
  let fromDb (user: Database.User) : Domain.User = { Id = user.Id; Lang = user.Lang }

  let toDb (user: Domain.User) : Database.User =
    Database.User(Id = user.Id, Lang = user.Lang)

  let fromTg (user: Telegram.Bot.Types.User) : Domain.User =
    { Id = user.Id
      Lang = user.LanguageCode }

[<RequireQualifiedAccess>]
module UserConversion =
  let fromDb (conversion: Database.Conversion) : Domain.UserConversion =
    { ConversionId = conversion.Id
      UserId = conversion.UserId
      ReceivedMessageId = conversion.ReceivedMessageId
      SentMessageId = conversion.SentMessageId }

  let toDb (conversion: Domain.UserConversion) : Database.Conversion =
    Database.Conversion(
      Id = conversion.ConversionId,
      UserId = conversion.UserId,
      ReceivedMessageId = conversion.ReceivedMessageId,
      SentMessageId = conversion.SentMessageId
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
    let fromDb (conversion: Database.Conversion) : Domain.Conversion.Prepared =
      match conversion.State with
      | Database.ConversionState.Prepared ->
        { Id = conversion.Id
          InputFile = conversion.InputFileName }

    let toDb (conversion: Domain.Conversion.Prepared) : Database.Conversion =
      Database.Conversion(Id = conversion.Id, InputFileName = conversion.InputFile, State = Database.ConversionState.Prepared)

  [<RequireQualifiedAccess>]
  module Converted =
    let fromDb (conversion: Database.Conversion) : Domain.Conversion.Converted =
      match conversion.State with
      | Database.ConversionState.Converted ->
        { Id = conversion.Id
          OutputFile = conversion.OutputFileName }

    let toDb (conversion: Domain.Conversion.Converted) : Database.Conversion =
      Database.Conversion(Id = conversion.Id, OutputFileName = conversion.OutputFile, State = Database.ConversionState.Converted)

  [<RequireQualifiedAccess>]
  module Thumbnailed =
    let fromDb (conversion: Database.Conversion) : Domain.Conversion.Thumbnailed =
      match conversion.State with
      | Database.ConversionState.Thumbnailed ->
        { Id = conversion.Id
          ThumbnailName = conversion.ThumbnailFileName }

    let toDb (conversion: Domain.Conversion.Thumbnailed) : Database.Conversion =
      Database.Conversion(Id = conversion.Id, ThumbnailFileName = conversion.ThumbnailName, State = Database.ConversionState.Thumbnailed)

  [<RequireQualifiedAccess>]
  module PreparedOrConverted =
    let fromDb (conversion: Database.Conversion) : Domain.Conversion.PreparedOrConverted =
      match conversion.State with
      | Database.ConversionState.Prepared -> Prepared.fromDb conversion |> Choice1Of2
      | Database.ConversionState.Converted -> Converted.fromDb conversion |> Choice2Of2

  [<RequireQualifiedAccess>]
  module PreparedOrThumbnailed =
    let fromDb (conversion: Database.Conversion) : Domain.Conversion.PreparedOrThumbnailed =
      match conversion.State with
      | Database.ConversionState.Prepared -> Prepared.fromDb conversion |> Choice1Of2
      | Database.ConversionState.Thumbnailed -> Thumbnailed.fromDb conversion |> Choice2Of2

  [<RequireQualifiedAccess>]
  module Completed =
    let fromDb (conversion: Database.Conversion) : Domain.Conversion.Completed =
      match conversion.State with
      | Database.ConversionState.Completed ->
        { Id = conversion.Id
          OutputFile = conversion.OutputFileName
          ThumbnailFile = conversion.ThumbnailFileName }

    let toDb (conversion: Domain.Conversion.Completed) : Database.Conversion =
      Database.Conversion(
        Id = conversion.Id,
        OutputFileName = conversion.OutputFile,
        ThumbnailFileName = conversion.ThumbnailFile,
        State = Database.ConversionState.Completed
      )
