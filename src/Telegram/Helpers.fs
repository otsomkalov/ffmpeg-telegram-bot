module Telegram.Helpers

open System.Text.RegularExpressions

[<RequireQualifiedAccess>]
module Func =
  let wrap2 f = fun x y -> f (x, y)

let (|Regex|_|) (regex: Regex) (text: string) =
  let matches = regex.Matches text

  if matches |> Seq.isEmpty then
    None
  else
    matches |> Seq.map _.Value |> Some