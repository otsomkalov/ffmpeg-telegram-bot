namespace Telegram.Infrastructure

open Telegram.Core

module Core =
  [<RequireQualifiedAccess>]
  module UserMessageId =
    let value (UserMessageId id) = id

