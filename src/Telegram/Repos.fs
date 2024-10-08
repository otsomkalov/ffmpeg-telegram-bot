namespace Telegram

open System.Threading.Tasks
open Domain.Core
open Telegram.Core
open otsom.fs.Telegram.Bot.Core

module Repos =
  [<RequireQualifiedAccess>]
  module UserConversion =
    type Load = ConversionId -> Task<UserConversion>
    type Save = UserConversion -> Task<unit>

  [<RequireQualifiedAccess>]
  module User =
    type Load = UserId -> Task<User option>
    type Create = UserId -> string option -> Task<User>

  [<RequireQualifiedAccess>]
  module Channel =
    type Load = ChannelId -> Task<Channel option>
    type Create = ChannelId -> Task<Channel>