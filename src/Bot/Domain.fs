module Bot.Domain

type User = { Id: int64; Lang: string }

type UserConversion =
  { ReceivedMessageId: int
    SentMessageId: int
    ConversionId: string
    UserId: int64 }

[<RequireQualifiedAccess>]
module Conversion =
  type New = { Id: string }

  type Prepared = { Id: string; InputFile: string }

  type Converted = { Id: string; OutputFile: string }

  type Thumbnailed = { Id: string; ThumbnailName: string }

  type PreparedOrConverted = Choice<Prepared, Converted>

  type PreparedOrThumbnailed = Choice<Prepared, Thumbnailed>

  type Completed =
    { Id: string
      OutputFile: string
      ThumbnailFile: string }
