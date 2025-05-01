namespace Telegram.Infrastructure

open System.IO
open System.Text.RegularExpressions
open Telegram.Core
open Telegram.Infrastructure.Settings
open System.Threading.Tasks
open Telegram.Infrastructure.Helpers
open otsom.fs.Extensions.String

module Workflows =
  let parseCommand (settings: InputValidationSettings) : ParseCommand =
    let linkRegex = Regex(settings.LinkRegex)

    fun message ->
      match message with
      | FromBot -> None |> Task.FromResult
      | Text messageText ->
        match messageText with
        | StartsWith "/start" -> Command.Start |> Some |> Task.FromResult
        | Regex linkRegex matches -> matches |> Command.Links |> Some |> Task.FromResult
        | _ -> None |> Task.FromResult
      | Document settings.MimeTypes doc -> Command.Document(doc.FileId, doc.FileName) |> Some |> Task.FromResult
      | Video settings.MimeTypes vid ->
        let videoName =
          vid.FileName
          |> Option.ofObj
          |> Option.defaultWith (fun _ ->
            let tmpFile = Path.GetTempFileName()
            let fileInfo = FileInfo(tmpFile)

            fileInfo.Name)

        Command.Video(vid.FileId, videoName) |> Some |> Task.FromResult
      | _ -> None |> Task.FromResult