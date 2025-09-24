module Bot.Mappings

open Telegram
open Telegram.Bot.Types
open Telegram.Bot.Types.Enums
open otsom.fs.Bot

let private mapDoc (message: Message) : Doc option =
  message.Document
  |> Option.ofObj
  |> Option.map (fun doc ->
    { Id = doc.FileId
      Name = doc.FileName
      MimeType = doc.MimeType
      Caption = message.Caption |> Option.ofObj })

let private mapVid (message: Message) : Vid option =
  message.Video
  |> Option.ofObj
  |> Option.map (fun vid ->
    { Id = vid.FileId
      Name = vid.FileName |> Option.ofObj
      MimeType = vid.MimeType
      Caption = message.Caption |> Option.ofObj })

let private mapMsg (message: Message) =
  match message.From |> Option.ofObj with
  | Some sender when sender.IsBot -> BotMsg
  | sender ->
    UserMsg
      { ChatId = message.Chat.Id |> ChatId
        Lang = sender |> Option.bind (fun u -> u.LanguageCode |> Option.ofObj)
        MessageId = message.MessageId |> ChatMessageId
        Text = message.Text |> Option.ofObj
        Doc = mapDoc message
        Vid = mapVid message }

type Update with
  member this.ToBot() =
    match this.Type with
    | UpdateType.Message -> Update.Msg(mapMsg this.Message)
    | UpdateType.ChannelPost -> Update.Msg(mapMsg this.ChannelPost)
    | _ -> Update.Other(string this.Type)