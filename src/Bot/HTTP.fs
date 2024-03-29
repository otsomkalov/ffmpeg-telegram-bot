[<RequireQualifiedAccess>]
module Bot.HTTP

open System
open System.Net
open System.Net.Http
open System.Threading.Tasks
open Infrastructure.Settings

type DownloadLinkError =
  | Unauthorized
  | NotFound
  | ServerError

type DownloadLink = string -> Task<Result<string, DownloadLinkError>>

let downloadLink (httpClientFactory: IHttpClientFactory) (workersSettings: WorkersSettings) : DownloadLink =
  let getBlobStream = Telegram.getBlobStream workersSettings

  fun link ->
    task {
      use client = httpClientFactory.CreateClient()
      use request = new HttpRequestMessage(HttpMethod.Get, link)
      use! response = client.SendAsync(request)

      return!
        match response.StatusCode with
        | HttpStatusCode.Unauthorized -> Unauthorized |> Error |> Task.FromResult
        | HttpStatusCode.NotFound -> NotFound |> Error |> Task.FromResult
        | HttpStatusCode.InternalServerError -> ServerError |> Error |> Task.FromResult
        | _ ->
          task {
            let fileName = link |> Uri |> (_.Segments) |> Seq.last

            use! converterBlobStream = getBlobStream fileName Telegram.Converter
            use! thumbnailerBlobStream = getBlobStream fileName Telegram.Thumbnailer

            do! response.Content.CopyToAsync(converterBlobStream)
            do! response.Content.CopyToAsync(thumbnailerBlobStream)

            return Ok(fileName)
          }
    }
