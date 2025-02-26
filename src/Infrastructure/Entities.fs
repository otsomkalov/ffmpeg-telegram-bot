﻿[<RequireQualifiedAccess>]
module Infrastructure.Entities

open System
open Domain.Core
open Infrastructure.Core
open MongoDB.Bson.Serialization.Attributes

type ConversionState =
  | New = 1
  | Prepared = 2
  | Converted = 3
  | Thumbnailed = 4
  | Completed = 5

type Conversion() =
  [<BsonId; BsonElement>]
  member val Id: string = "" with get, set

  [<BsonElement>]
  member val ChatId: int64 = 0L with get, set

  [<BsonElement>]
  member val UserId: int64 Nullable = Nullable() with get, set

  [<BsonElement>]
  member val ReceivedMessageId: int = 0 with get, set

  [<BsonElement>]
  member val SentMessageId: int = 0 with get, set

  [<BsonElement>]
  member val InputFileName: string = "" with get, set

  [<BsonElement>]
  member val OutputFileName: string = "" with get, set

  [<BsonElement>]
  member val ThumbnailFileName: string = "" with get, set

  [<BsonElement>]
  member val State: ConversionState = ConversionState.New with get, set

  [<BsonElement>]
  member val CreatedAt: DateTime = DateTime.Now with get

  member this.ToNew(): Conversion.New = { Id = (this.Id |> ConversionId) }

  member this.ToPrepared(): Conversion.Prepared =
    { Id = (this.Id |> ConversionId)
      InputFile = this.InputFileName }

  member this.ToConverted(): Conversion.Converted =
    { Id = (this.Id |> ConversionId)
      OutputFile = this.OutputFileName }

  member this.ToThumbnailed(): Conversion.Thumbnailed =
    { Id = (this.Id |> ConversionId)
      ThumbnailName = this.ThumbnailFileName }

  member this.ToCompleted(): Conversion.Completed =
    { Id = (this.Id |> ConversionId)
      OutputFile = (this.OutputFileName |> Conversion.Video)
      ThumbnailFile = (this.ThumbnailFileName |> Conversion.Thumbnail) }

  member this.ToDomain: Domain.Core.Conversion =
    match this.State with
    | ConversionState.New -> New (this.ToNew())
    | ConversionState.Prepared -> Prepared (this.ToPrepared())
    | ConversionState.Converted -> Converted (this.ToConverted())
    | ConversionState.Thumbnailed -> Thumbnailed (this.ToThumbnailed())
    | ConversionState.Completed -> Completed (this.ToCompleted())

  static member FromNew(conversion: Conversion.New) : Conversion =
    Conversion(Id = conversion.Id.Value, State = ConversionState.New)

  static member FromPrepared(conversion: Conversion.Prepared) : Conversion =
    Conversion(Id = conversion.Id.Value, State = ConversionState.Prepared, InputFileName = conversion.InputFile)

  static member FromConverted(conversion: Conversion.Converted) : Conversion =
    Conversion(Id = conversion.Id.Value, State = ConversionState.Converted, OutputFileName = conversion.OutputFile)

  static member FromThumbnailed(conversion: Conversion.Thumbnailed) : Conversion =
    Conversion(Id = conversion.Id.Value, State = ConversionState.Thumbnailed, ThumbnailFileName = conversion.ThumbnailName)

  static member FromCompleted(conversion: Conversion.Completed) : Conversion =
    Conversion(
      Id = conversion.Id.Value,
      State = ConversionState.Completed,
      OutputFileName = (conversion.OutputFile |> Conversion.Video.value),
      ThumbnailFileName = (conversion.ThumbnailFile |> Conversion.Thumbnail.value)
    )

  static member FromDomain(conversion: Domain.Core.Conversion) : Conversion =
    match conversion with
    | New conversion -> Conversion.FromNew conversion
    | Prepared conversion -> Conversion.FromPrepared conversion
    | Converted conversion -> Conversion.FromConverted conversion
    | Thumbnailed conversion -> Conversion.FromThumbnailed conversion
    | Completed conversion -> Conversion.FromCompleted conversion