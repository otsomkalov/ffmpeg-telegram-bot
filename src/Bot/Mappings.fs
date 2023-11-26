[<RequireQualifiedAccess>]
module Bot.Mappings

[<RequireQualifiedAccess>]
module NewConversion =
  let fromDb (conversion: Database.Conversion) : Domain.NewConversion =
    match conversion.State with
    | Database.ConversionState.New ->
      { Id = conversion.Id
        UserId = conversion.UserId
        ReceivedMessageId = conversion.ReceivedMessageId
        SentMessageId = conversion.SentMessageId }

  let toDb (conversion: Domain.NewConversion) : Database.Conversion =
    Database.Conversion(
      Id = conversion.Id,
      UserId = conversion.UserId,
      ReceivedMessageId = conversion.ReceivedMessageId,
      SentMessageId = conversion.SentMessageId,
      State = Database.ConversionState.New
    )

[<RequireQualifiedAccess>]
module Conversion =
  let fromDb (conversion: Database.Conversion) : Domain.Conversion =
    { Id = conversion.Id
      UserId = conversion.UserId
      ReceivedMessageId = conversion.ReceivedMessageId
      SentMessageId = conversion.SentMessageId
      State =
        match conversion.State with
        | Database.ConversionState.Prepared -> Domain.ConversionState.Prepared conversion.InputFileName
        | Database.ConversionState.Converted -> Domain.ConversionState.Converted conversion.OutputFileName
        | Database.ConversionState.Thumbnailed -> Domain.ConversionState.Thumbnailed conversion.ThumbnailFileName
        | Database.ConversionState.Completed -> Domain.ConversionState.Completed(conversion.OutputFileName, conversion.ThumbnailFileName) }

  let toDb (conversion: Domain.Conversion) : Database.Conversion =
    let entity =
      Database.Conversion(
        Id = conversion.Id,
        UserId = conversion.UserId,
        ReceivedMessageId = conversion.ReceivedMessageId,
        SentMessageId = conversion.SentMessageId
      )

    do
      match conversion.State with
      | Domain.Prepared inputFileName ->
        entity.InputFileName <- inputFileName
        entity.State <- Database.ConversionState.Prepared
      | Domain.Converted outputFileName ->
        entity.OutputFileName <- outputFileName
        entity.State <- Database.ConversionState.Converted
      | Domain.Thumbnailed thumbnailFileName ->
        entity.ThumbnailFileName <- thumbnailFileName
        entity.State <- Database.ConversionState.Thumbnailed
      | Domain.Completed(outputFileName, thumbnailFileName) ->
        entity.OutputFileName <- outputFileName
        entity.ThumbnailFileName <- thumbnailFileName
        entity.State <- Database.ConversionState.Completed

    entity
