namespace Bot

open System.Threading.Tasks
open Microsoft.FSharp.Core

type Translation ={
  Key: string
  Value: string
}

module Translation =
  [<Literal>]
  let DefaultLang = "en"

  type GetTranslation = string -> string
  type FormatTranslation = string * obj array -> string

  type GetLocaleTranslations = string option -> Task<GetTranslation * FormatTranslation>

  [<RequireQualifiedAccess>]
  module Resources =
    [<Literal>]
    let Welcome = "welcome"
    [<Literal>]
    let LinkDownload = "link-download"
    [<Literal>]
    let DocumentDownload="document-download"
    [<Literal>]
    let VideoDownload="video-download"
    [<Literal>]
    let ConversionInProgress = "conversion-in-progress"
    [<Literal>]
    let NotAuthorized = "not-authorized"
    [<Literal>]
    let NotFound = "not-found"
    [<Literal>]
    let ServerError = "server-error"
    [<Literal>]
    let VideoConverted = "video-converted"
    [<Literal>]
    let ThumbnailGenerated = "thumbnail-generated"
    [<Literal>]
    let Uploading = "uploading"