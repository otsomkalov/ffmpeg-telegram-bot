namespace Telegram.Repos

open System.Threading
open System.Threading.Tasks
open Domain.Core
open Telegram.Core
open otsom.fs.Telegram.Bot.Core

type ILoadGroup =
  abstract LoadGroup: GroupId -> Task<Group option>

type ISaveGroup =
  abstract SaveGroup: Group -> Task<unit>

type IGroupRepo =
  inherit ILoadGroup
  inherit ISaveGroup

type ILoadChannel =
  abstract LoadChannel: ChannelId -> Task<Channel option>

type ISaveChannel =
  abstract SaveChannel: Channel -> Task<unit>

type IChannelRepo =
  inherit ILoadChannel
  inherit ISaveChannel

type ILoadUser =
  abstract LoadUser: UserId * CancellationToken -> Task<User option>

type ISaveUser =
  abstract SaveUser: User -> Task<unit>

type IUserRepo =
  inherit ILoadUser
  inherit ISaveUser

type ILoadUserConversion =
  abstract LoadUserConversion: ConversionId -> Task<UserConversion>

type ISaveUserConversion =
  abstract SaveUserConversion: UserConversion -> Task<unit>

type IUserConversionRepo =
  inherit ILoadUserConversion
  inherit ISaveUserConversion