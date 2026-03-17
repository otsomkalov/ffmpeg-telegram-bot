module Tests.Conversion.Prepared

open System
open Domain
open Domain.Core
open Domain.Core.Conversion
open Moq
open Xunit
open FsUnit.Xunit

[<Fact>]
let ``Converted file successfully added to Prepared conversion`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testInputFile = "test-file.webm"
  let testOutput = Video "test-output.mp4"

  let input: Prepared =
    { Id = conversionId
      InputFile = testInputFile }

  let expected =
    { Id = conversionId
      OutputFile = testOutput }

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(Conversion.Converted expected)).ReturnsAsync(())

  let sut: IConversionService = ConversionService(repo.Object)

  task {
    let! result = sut.SaveVideo(input, testOutput)

    result |> should equal expected

    repo.VerifyAll()
  }

[<Fact>]
let ``Thumbnail successfully added to Prepared conversion`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testInputFile = "test-file.webm"
  let testThumbnail = Thumbnail "test-thumbnail.jpg"

  let input: Prepared =
    { Id = conversionId
      InputFile = testInputFile }

  let expected =
    { Id = conversionId
      ThumbnailName = testThumbnail }

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(Conversion.Thumbnailed expected)).ReturnsAsync(())

  let sut: IConversionService = ConversionService(repo.Object)

  task {
    let! result = sut.SaveThumbnail(input, testThumbnail)

    result |> should equal expected

    repo.VerifyAll()
  }