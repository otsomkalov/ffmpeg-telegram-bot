namespace Telegram

open System.Threading.Tasks
open Domain.Core
open otsom.fs.Telegram.Bot.Core

module Core=
  type ChatId = ChatId of int64
  type UserMessageId = UserMessageId of int
  type UploadCompletedConversion = ConversionId -> Task<unit>

  type UserConversion =
    { ReceivedMessageId: UserMessageId
      SentMessageId: BotMessageId
      ConversionId: string
      UserId: UserId option
      ChatId: UserId }