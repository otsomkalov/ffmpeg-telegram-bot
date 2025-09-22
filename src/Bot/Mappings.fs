module Bot.Mappings

open Telegram
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums

type Update with
  member this.ToBot() =
    match this.Type with
    | UpdateType.Message -> Update.Msg this.Message
    | UpdateType.ChannelPost -> Update.Msg this.ChannelPost
    | _ -> Update.Other