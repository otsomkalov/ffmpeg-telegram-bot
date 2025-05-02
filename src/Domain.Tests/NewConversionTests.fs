module Tests.Conversion.New

open System
open Domain
open Domain.Core
open Domain.Core.Conversion
open Domain.Workflows
open Moq
open Xunit
open FsUnit.Xunit

[<Fact>]
let ``New Conversion is created and saved`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId

  let expected = { Id = conversionId }

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(New expected)).ReturnsAsync(())
  repo.Setup(_.GenerateConversionId()).Returns(conversionId)

  let sut = Conversion.create repo.Object

  task {
    let! result = sut ()

    result |> should equal expected

    repo.VerifyAll()
  }

[<Fact>]
let ``Prepare New Conversion downloads document and saves it`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let docName = "document-name.webm"

  let doc: New.InputDocument = { Id = "document-id"; Name = docName }

  let expectedConversion =
    { Id = conversionId
      InputFile = docName }

  let expectedResult: Result<_, Conversion.New.DownloadLinkError> =
    Ok(expectedConversion)

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(Prepared expectedConversion)).ReturnsAsync(())
  repo.Setup(_.DownloadDocument(doc)).ReturnsAsync(docName)
  repo.Setup(_.QueueConversion(expectedConversion)).ReturnsAsync(())
  repo.Setup(_.QueueThumbnailing(expectedConversion)).ReturnsAsync(())

  let sut: IConversionService = ConversionService(repo.Object)

  task {
    let! result = sut.PrepareConversion(conversionId, New.InputFile.Document(doc))

    result |> should equal expectedResult

    repo.VerifyAll()
  }

[<Fact>]
let ``Prepare New Conversion downloads link and saves it`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testLink = "http://test.com/document-name.webm"

  let expectedConversion =
    { Id = conversionId
      InputFile = "document-name.webm" }

  let link: New.InputLink = { Url = testLink }

  let expectedResult: Result<_, Conversion.New.DownloadLinkError> =
    Ok(expectedConversion)

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(Prepared expectedConversion)).ReturnsAsync(())
  repo.Setup(_.DownloadLink(link)).ReturnsAsync(Ok expectedConversion.InputFile)
  repo.Setup(_.QueueConversion(expectedConversion)).ReturnsAsync(())
  repo.Setup(_.QueueThumbnailing(expectedConversion)).ReturnsAsync(())

  let sut: IConversionService = ConversionService(repo.Object)

  task {
    let! result = sut.PrepareConversion(conversionId, New.InputFile.Link(link))

    result |> should equal expectedResult

    repo.VerifyAll()
  }

[<Fact>]
let ``Prepare New Conversion stops if link file not found`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testLink = "http://test.com/document-name.webm"

  let link: New.InputLink = { Url = testLink }

  let expected = Error New.DownloadLinkError.NotFound

  let expectedResult: Result<Conversion.Prepared, _> = expected

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.DownloadLink(link)).ReturnsAsync(expected)

  let sut: IConversionService = ConversionService(repo.Object)

  task {
    let! result = sut.PrepareConversion(conversionId, New.InputFile.Link(link))

    result |> should equal expectedResult

    repo.VerifyAll()
  }

[<Fact>]
let ``Prepare New Conversion stops if unauthorized to download link file`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testLink = "http://test.com/document-name.webm"

  let expected = Error New.DownloadLinkError.Unauthorized
  let expectedResult: Result<Conversion.Prepared, _> = expected

  let link: New.InputLink = { Url = testLink }

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.DownloadLink(link)).ReturnsAsync(expected)

  let sut: IConversionService = ConversionService(repo.Object)

  task {
    let! result = sut.PrepareConversion(conversionId, New.InputFile.Link(link))

    result |> should equal expectedResult

    repo.VerifyAll()
  }

[<Fact>]
let ``Prepare New Conversion stops if internal server error happened during the link file download`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testLink = "http://test.com/document-name.webm"

  let expected = Error New.DownloadLinkError.ServerError

  let link: New.InputLink = { Url = testLink }

  let expectedResult: Result<Conversion.Prepared, _> = expected

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.DownloadLink(link)).ReturnsAsync(expected)

  let sut: IConversionService = ConversionService(repo.Object)

  task {
    let! result = sut.PrepareConversion(conversionId, Conversion.New.InputFile.Link(link))

    result |> should equal expectedResult

    repo.VerifyAll()
  }