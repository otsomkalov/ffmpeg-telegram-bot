namespace Bot.Repos

open System.Threading.Tasks
open Bot
open Bot.Core
open Domain.Core
open otsom.fs.Bot

type ILoadUserConversion =
  abstract LoadUserConversion: ConversionId -> Task<UserConversion>

type ISaveUserConversion =
  abstract SaveUserConversion: UserConversion -> Task<unit>

type IUserConversionRepo =
  inherit ILoadUserConversion
  inherit ISaveUserConversion

type ILoadChat =
  abstract LoadChat: ChatId -> Task<Chat option>

type ISaveChat =
  abstract SaveChat: Chat -> Task<unit>

type IChatRepo =
  inherit ILoadChat
  inherit ISaveChat