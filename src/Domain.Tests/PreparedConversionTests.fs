module Tests.Conversion.Prepared

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
let ``Converted file successfully added to Prepared conversion`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testInputFile = "test-file.webm"
  let testOutput = "test-output.mp4"

  let expected =
    { Id = conversionId
      OutputFile = testOutput }

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(Conversion.Converted expected)).ReturnsAsync(())

  let sut = Conversion.Prepared.saveVideo repo.Object

  task {
    let! result =
      sut
        { Id = conversionId
          InputFile = testInputFile }
        testOutput

    result |> should equal expected

    repo.VerifyAll()
  }

[<Fact>]
let ``Thumbnail successfully added to Prepared conversion`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testInputFile = "test-file.webm"
  let testThumbnail = "test-thumbnail.jpg"

  let expected =
    { Id = conversionId
      ThumbnailName = testThumbnail }

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(Conversion.Thumbnailed expected)).ReturnsAsync(())

  let sut = Conversion.Prepared.saveThumbnail repo.Object

  task {
    let! result =
      sut
        { Id = conversionId
          InputFile = testInputFile }
        testThumbnail

    result |> should equal expected

    repo.VerifyAll()
  }