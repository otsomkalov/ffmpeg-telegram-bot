namespace Infrastructure

open System
open System.Diagnostics
open System.Net
open System.Net.Http
open Azure.Storage.Blobs
open Azure.Storage.Queues
open Domain
open Domain.Core
open Infrastructure.Core
open Infrastructure.Helpers
open Infrastructure.Queue
open Infrastructure.Settings
open Microsoft.Extensions.Options
open MongoDB.Bson
open MongoDB.Driver
open MongoDB.Driver.Linq
open Telegram.Bot
open otsom.fs.Extensions
open System.Threading.Tasks
open FsToolkit.ErrorHandling

type ConversionRepo
  (
    collection: IMongoCollection<Entities.Conversion>,
    options: IOptions<WorkersSettings>,
    httpClientFactory: IHttpClientFactory,
    bot: ITelegramBotClient
  ) =
  let settings = options.Value
  let blobServiceClient = BlobServiceClient(settings.ConnectionString)
  let queueServiceClient = QueueServiceClient(settings.ConnectionString)

  interface IConversionRepo with
    member this.GenerateConversionId() =
      ObjectId.GenerateNewId().ToString() |> ConversionId

    member _.LoadConversion(ConversionId id) =
      collection.AsQueryable().FirstOrDefaultAsync(fun c -> c.Id = ObjectId(id))
      |> Task.map _.ToDomain

    member _.SaveConversion conversion =
      let filter =
        Builders<Entities.Conversion>.Filter.Eq(_.Id, ObjectId(conversion.Id.Value))

      collection.ReplaceOneAsync(filter, Entities.Conversion.FromDomain conversion, ReplaceOptions(IsUpsert = true))
      |> Task.map ignore

    member this.DeleteThumbnail(Conversion.Thumbnail thumbnail) =
      let container =
        blobServiceClient.GetBlobContainerClient settings.Thumbnailer.Output.Container

      let blob = container.GetBlobClient(thumbnail)

      blob.DeleteAsync() |> Task.map ignore

    member this.DeleteVideo(Conversion.Video video) =
      let container =
        blobServiceClient.GetBlobContainerClient settings.Converter.Output.Container

      let blob = container.GetBlobClient(video)

      blob.DeleteAsync() |> Task.map ignore

    member this.QueueConversion(conversion) =
      let queueClient = queueServiceClient.GetQueueClient(settings.Converter.Input.Queue)

      let message: BaseMessage<Conversion.Prepared.ConverterMessage> =
        { OperationId = Activity.Current.ParentId
          Data =
            { Id = conversion.Id.Value
              Name = conversion.InputFile } }

      message |> JSON.serialize |> queueClient.SendMessageAsync |> Task.map ignore

    member this.QueueThumbnailing(conversion) =
      let queueClient =
        queueServiceClient.GetQueueClient(settings.Thumbnailer.Input.Queue)

      let message: BaseMessage<Conversion.Prepared.ConverterMessage> =
        { OperationId = Activity.Current.ParentId
          Data =
            { Id = conversion.Id.Value
              Name = conversion.InputFile } }

      message |> JSON.serialize |> queueClient.SendMessageAsync |> Task.map ignore

    member this.DownloadDocument(document) =
      task {
        use! converterBlobStream = Storage.getBlobStream settings document.Name settings.Converter.Input.Container

        do! bot.GetInfoAndDownloadFile(document.Id, converterBlobStream) |> Task.ignore

        use! thumbnailerBlobStream = Storage.getBlobStream settings document.Name settings.Thumbnailer.Input.Container

        do! bot.GetInfoAndDownloadFile(document.Id, thumbnailerBlobStream) |> Task.ignore

        return document.Name
      }

    member this.DownloadLink(link) =
      let getBlobStream = Storage.getBlobStream settings

      task {
        use client = httpClientFactory.CreateClient()
        use request = new HttpRequestMessage(HttpMethod.Get, link.Url)
        use! response = client.SendAsync(request)

        match response.StatusCode with
        | HttpStatusCode.Unauthorized -> return Error Conversion.New.DownloadLinkError.Unauthorized
        | HttpStatusCode.NotFound -> return Error Conversion.New.DownloadLinkError.NotFound
        | HttpStatusCode.InternalServerError -> return Error Conversion.New.DownloadLinkError.ServerError
        | _ ->
          let fileName = link.Url |> Uri |> _.Segments |> Seq.last

          use! converterBlobStream = getBlobStream fileName settings.Converter.Input.Container
          use! thumbnailerBlobStream = getBlobStream fileName settings.Thumbnailer.Input.Container

          do! response.Content.CopyToAsync(converterBlobStream)
          do! response.Content.CopyToAsync(thumbnailerBlobStream)

          return Ok(fileName)
      }

    member this.QueueUpload(conversion) =
      let queueClient = queueServiceClient.GetQueueClient(settings.Uploader.Queue)

      let messageBody =
        JSON.serialize
          { OperationId = Activity.Current.ParentId
            Data = { ConversionId = conversion.Id.Value } }

      queueClient.SendMessageAsync(messageBody) |> Task.ignore

    member this.QueuePreparation(conversionId, inputFile) =
      let queueClient = queueServiceClient.GetQueueClient(settings.Downloader.Queue)

      let message =
        { OperationId = Activity.Current.ParentId
          Data =
            { ConversionId = conversionId
              File = inputFile } }

      let messageBody = JSON.serialize message

      queueClient.SendMessageAsync(messageBody) |> Task.ignore