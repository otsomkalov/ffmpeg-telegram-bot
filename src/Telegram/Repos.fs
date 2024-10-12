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
    type Create = User -> Task<unit>

  [<RequireQualifiedAccess>]
  module Channel =
    type Load = ChannelId -> Task<Channel option>
    type Save = Channel -> Task<unit>

  [<RequireQualifiedAccess>]
  module Group =
    type Load = GroupId -> Task<Group option>

    type Save = Group -> Task<unit>