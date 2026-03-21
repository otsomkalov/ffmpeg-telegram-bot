module Tests.Conversion.Converted

open System
open Domain
open Domain.Core
open Domain.Core.Conversion
open Moq
open Xunit
open FsUnit.Xunit

[<Fact>]
let ``Converted conversion completes with thumbnail`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testOutput = Video "test-output.mp4"
  let testThumbnail = Thumbnail "test-thumbnail.jpg"

  let input: Converted =
    { Id = conversionId
      OutputFile = testOutput }

  let expected =
    { Id = conversionId
      OutputFile = testOutput
      ThumbnailFile = testThumbnail }

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(Conversion.Completed expected)).ReturnsAsync(())

  let sut: IConversionService = ConversionService(repo.Object)

  task {
    let! result = sut.CompleteConversion(input, testThumbnail)

    result |> should equal expected

    repo.VerifyAll()
  }