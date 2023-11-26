[<RequireQualifiedAccess>]
module Bot.Domain

type NewConversion =
  { Id: string
    ReceivedMessageId: int
    SentMessageId: int
    UserId: int64 }

type ConversionState =
  | Prepared of inputFileName: string
  | Converted of outputFileName: string
  | Thumbnailed of thumbnailFileName: string
  | Completed of outputFileName: string * thumbnailFileName: string

type Conversion =
  { Id: string
    ReceivedMessageId: int
    SentMessageId: int
    UserId: int64
    State: ConversionState }
