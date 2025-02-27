module Tests.Conversion.New

open System
open System.Threading.Tasks
open Domain
open Domain.Core
open Domain.Core.Conversion
open Domain.Repos
open Domain.Workflows
open Moq
open Xunit
open FsUnit.Xunit

[<Fact>]
let ``New Conversion is created and saved`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId

  let generateId () = conversionId

  let expected = { Id = conversionId }

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(New expected)).ReturnsAsync(())

  let sut = Conversion.create generateId repo.Object

  task {
    let! result = sut ()

    result |> should equal expected

    repo.VerifyAll()
  }

[<Fact>]
let ``Prepare New Conversion downloads document and saves it`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let docName = "document-name.webm"
  let docId = "document-id"

  let expectedConversion =
    { Id = conversionId
      InputFile = docName }

  let downloadLink _ = failwith "todo"

  let downloadDocument (doc: New.InputDocument) =
    doc.Id |> should equal docId
    docName |> should equal docName
    docName |> Task.FromResult

  let queueConversion c =
    c |> should equal expectedConversion
    Task.FromResult()

  let queueThumbnailing c =
    c |> should equal expectedConversion
    Task.FromResult()

  let expectedResult: Result<_, Conversion.New.DownloadLinkError> =
    Ok(expectedConversion)

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(Prepared expectedConversion)).ReturnsAsync(())

  let sut =
    Conversion.New.prepare downloadLink downloadDocument repo.Object queueConversion queueThumbnailing

  task {
    let! result = sut conversionId (Conversion.New.InputFile.Document({ Id = docId; Name = docName }))

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

  let downloadLink (link: New.InputLink) =
    link.Url |> should equal testLink
    testLink |> Uri |> _.Segments |> Seq.last |> Ok |> Task.FromResult

  let downloadDocument _ = failwith "todo"

  let queueConversion c =
    c |> should equal expectedConversion
    Task.FromResult()

  let queueThumbnailing c =
    c |> should equal expectedConversion
    Task.FromResult()

  let expectedResult: Result<_, Conversion.New.DownloadLinkError> =
    Ok(expectedConversion)

  let repo = Mock<IConversionRepo>()

  repo.Setup(_.SaveConversion(Prepared expectedConversion)).ReturnsAsync(())

  let sut =
    Conversion.New.prepare downloadLink downloadDocument repo.Object queueConversion queueThumbnailing

  task {
    let! result = sut conversionId (Conversion.New.InputFile.Link({ Url = testLink }))

    result |> should equal expectedResult

    repo.VerifyAll()
  }

[<Fact>]
let ``Prepare New Conversion stops if link file not found`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testLink = "http://test.com/document-name.webm"

  let downloadLink (link: New.InputLink) =
    link.Url |> should equal testLink
    New.DownloadLinkError.NotFound |> Error |> Task.FromResult

  let downloadDocument _ = failwith "todo"

  let queueConversion _ = failwith "todo"

  let queueThumbnailing _ = failwith "todo"

  let expectedResult: Result<Conversion.Prepared, _> =
    New.DownloadLinkError.NotFound |> Error

  let repo = Mock<IConversionRepo>()

  let sut =
    Conversion.New.prepare downloadLink downloadDocument repo.Object queueConversion queueThumbnailing

  task {
    let! result = sut conversionId (Conversion.New.InputFile.Link({ Url = testLink }))

    result |> should equal expectedResult

    repo.VerifyNoOtherCalls()
  }

[<Fact>]
let ``Prepare New Conversion stops if unauthorized to download link file`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testLink = "http://test.com/document-name.webm"

  let downloadLink (link: New.InputLink) =
    link.Url |> should equal testLink
    New.DownloadLinkError.Unauthorized |> Error |> Task.FromResult

  let downloadDocument _ = failwith "todo"

  let saveConversion _ = failwith "todo"

  let queueConversion _ = failwith "todo"

  let queueThumbnailing _ = failwith "todo"

  let expectedResult: Result<Conversion.Prepared, _> =
    New.DownloadLinkError.Unauthorized |> Error

  let repo = Mock<IConversionRepo>()

  let sut =
    Conversion.New.prepare downloadLink downloadDocument repo.Object queueConversion queueThumbnailing

  task {
    let! result = sut conversionId (Conversion.New.InputFile.Link({ Url = testLink }))

    result |> should equal expectedResult

    repo.VerifyNoOtherCalls()
  }

[<Fact>]
let ``Prepare New Conversion stops if internal server error happened during the link file download`` () =
  let conversionId = Guid.NewGuid().ToString() |> ConversionId
  let testLink = "http://test.com/document-name.webm"

  let downloadLink (link: New.InputLink) =
    link.Url |> should equal testLink
    New.DownloadLinkError.ServerError |> Error |> Task.FromResult

  let downloadDocument _ = failwith "todo"

  let saveConversion _ = failwith "todo"

  let queueConversion _ = failwith "todo"

  let queueThumbnailing _ = failwith "todo"

  let expectedResult: Result<Conversion.Prepared, _> =
    New.DownloadLinkError.ServerError |> Error

  let repo = Mock<IConversionRepo>()

  let sut =
    Conversion.New.prepare downloadLink downloadDocument repo.Object queueConversion queueThumbnailing

  task {
    let! result = sut conversionId (Conversion.New.InputFile.Link({ Url = testLink }))

    result |> should equal expectedResult

    repo.VerifyNoOtherCalls()
  }