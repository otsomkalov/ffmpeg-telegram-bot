module Telegram.Helpers

[<RequireQualifiedAccess>]
module Func =
  let wrap2 f = fun x y -> f (x, y)