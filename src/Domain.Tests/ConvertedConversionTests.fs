module Tests.Conversion.Converted

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
let ``Converted conversion completes with thumbnail`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testOutput = "test-output.mp4"
  let testThumbnail = "test-thumbnail.jpg"

  let expected =
    { Id = conversionId
      OutputFile = Video(testOutput)
      ThumbnailFile = Thumbnail(testThumbnail) }

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(Conversion.Completed expected)).ReturnsAsync(())

  let sut = Conversion.Converted.complete repo.Object

  task {
    let! result =
      sut
        { Id = conversionId
          OutputFile = testOutput }
        testThumbnail

    result |> should equal expected
  }