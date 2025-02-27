module Tests.Conversion.Thumbnailed

open System
open System.Threading.Tasks
open Domain
open Domain.Core
open Domain.Core.Conversion
open Domain.Workflows
open Moq
open Xunit
open FsUnit.Xunit

[<Fact>]
let ``Thumbnailed conversion completes with converted file`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testOutput = "test-output.mp4"
  let testThumbnail = "test-thumbnail.jpg"

  let expected =
    { Id = conversionId
      OutputFile = Video(testOutput)
      ThumbnailFile = Thumbnail(testThumbnail) }

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(Conversion.Completed expected)).ReturnsAsync(())

  let sut = Conversion.Thumbnailed.complete repo.Object

  task {
    let! result =
      sut
        { Id = conversionId
          ThumbnailName = testThumbnail }
        testOutput

    result |> should equal expected
  }
