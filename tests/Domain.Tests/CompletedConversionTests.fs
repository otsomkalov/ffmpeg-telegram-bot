module Tests.Conversion.Completed

open System
open Domain
open Domain.Core
open Domain.Core.Conversion
open Moq
open Xunit

[<Fact>]
let ``Completed conversion cleanup removes video and thumbnail`` () =

  let video = Video("test-output-video.mp4")
  let thumbnail = Thumbnail("test-output-thumbnail.jpg")

  let conversion: Core.Conversion.Completed =
    { Id = Guid.NewGuid().ToString() |> ConversionId
      OutputFile = video
      ThumbnailFile = thumbnail }

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.DeleteVideo(video)).ReturnsAsync(())

  repo.Setup(_.DeleteThumbnail(thumbnail)).ReturnsAsync(())

  let sut: IConversionService = ConversionService(repo.Object)

  task {
    do! sut.CleanupConversion conversion

    repo.VerifyAll()
  }