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
  let testOutput = Video "test-output.mp4"
  let testThumbnail = Thumbnail "test-thumbnail.jpg"

  let input: Thumbnailed =
    { Id = conversionId
      ThumbnailName = testThumbnail }

  let expected =
    { Id = conversionId
      OutputFile = testOutput
      ThumbnailFile = testThumbnail }

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(Conversion.Completed expected)).ReturnsAsync(())

  let sut: IConversionService = ConversionService(repo.Object)

  task {
    let! result = sut.CompleteConversion(input, testOutput)

    result |> should equal expected

    repo.VerifyAll()
  }