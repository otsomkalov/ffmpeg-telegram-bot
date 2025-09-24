module Telegram.Helpers

open System.Text.RegularExpressions

let (|Regex|_|) (regex: Regex) (text: string) =
  let matches = regex.Matches text

  if matches |> Seq.isEmpty then
    None
  else
    matches |> Seq.map _.Value |> Some