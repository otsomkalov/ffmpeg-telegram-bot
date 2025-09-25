module Telegram.Tests

open System
open System.Threading.Tasks
open Domain
open Domain.Core
open Domain.Core.Conversion
open Microsoft.Extensions.Logging
open Moq
open Telegram.Core
open Telegram.Repos
open Telegram.Settings
open Xunit
open Telegram.Handlers
open otsom.fs.Bot
open otsom.fs.Resources
open FsUnit.Xunit

let chatId = ChatId 1
let msgId = ChatMessageId 1
let botMsgId = BotMessageId 2
let conversionId = ConversionId(Guid.NewGuid().ToString())
let conversion = { Id = conversionId }

let settings: InputValidationSettings =
  { LinkRegex = "https?[^ ]*.webm\\??(?:&?[^=&]*=[^=&]*)*"
    MimeTypes = [ "video/webm" ] }

let msg =
  { MessageId = msgId
    Text = Some "/start"
    ChatId = chatId
    Lang = None
    Doc = None
    Vid = None }

let logger = Mock<ILogger<MsgHandler>>()

type StartHandler() =
  let botService = Mock<IBotService>()
  let resp = Mock<IResourceProvider>()

  do resp.Setup(fun r -> r[Resources.Welcome]).Returns(Resources.Welcome) |> ignore

  do
    botService.Setup(_.ReplyToMessage(msgId, Resources.Welcome)).ReturnsAsync(botMsgId)
    |> ignore

  let handler = startHandler botService.Object resp.Object

  [<Fact>]
  member _.``Sends welcome message on /start command``() =
    task {
      let! result = handler msg

      result |> should equal (Some())

      resp.VerifyAll()
      botService.VerifyAll()
    }

  [<Fact>]
  member _.``Does nothing on other commands``() =
    let msg = { msg with Text = Some "/other" }

    task {
      let! result = handler msg

      result |> should equal None
      resp.VerifyNoOtherCalls()
      botService.VerifyNoOtherCalls()
    }

type LinksHandler() =
  let botService = Mock<IBotService>()
  let resp = Mock<IResourceProvider>()
  let userConversionRepo = Mock<IUserConversionRepo>()
  let conversionRepo = Mock<IConversionRepo>()
  let createConversion: Create = fun _ -> Task.FromResult({ Id = conversionId })

  let link1 = "https://test.com/document-name1.webm"
  let link2 = "https://test.com/document-name2.webm"
  let link3 = "https://test.com/document-name3.mp4"

  do
    resp.Setup(fun r -> r[Resources.LinkDownload, [| link1 |]]).Returns(Resources.LinkDownload)
    |> ignore

  let sendMessageCall =
    botService.Setup(_.ReplyToMessage(msgId, Resources.LinkDownload)).ReturnsAsync(botMsgId)

  let handler =
    linksHandler createConversion userConversionRepo.Object conversionRepo.Object settings logger.Object botService.Object resp.Object

  [<Fact>]
  member _.``Sends message with link from message matching to the regex``() =
    let msg = { msg with Text = Some link1 }

    task {
      let! result = handler msg

      result |> should equal (Some())
      resp.VerifyAll()
      botService.VerifyAll()
      userConversionRepo.VerifyAll()
      conversionRepo.VerifyAll()
    }

  [<Fact>]
  member _.``Send multiple messages for each link from message matching to the regex``() =
    do resp.Setup(fun r -> r[Resources.LinkDownload, [| link2 |]]).Returns(Resources.LinkDownload)
    do sendMessageCall.Verifiable(Times.Exactly(2))

    let msg =
      { msg with
          Text = Some(sprintf "%s %s" link1 link2) }

    task {
      let! result = handler msg

      result |> should equal (Some())

      resp.VerifyAll()
      botService.VerifyAll()
      userConversionRepo.VerifyAll()
      conversionRepo.VerifyAll()
    }

  [<Fact>]
  member _.``Does nothing if message text contains link that doesn't match to the regex``() =
    let msg = { msg with Text = Some link3 }

    task {
      let! result = handler msg

      result |> should equal None
      resp.VerifyNoOtherCalls()
      botService.VerifyNoOtherCalls()
    }

  [<Fact>]
  member _.``Does nothing if message text has no links matching to the regex``() =
    let msg = { msg with Text = Some "some text" }

    task {
      let! result = handler msg

      result |> should equal None
      resp.VerifyNoOtherCalls()
      botService.VerifyNoOtherCalls()
    }

type DocHandler() =
  let docId = "doc-id"
  let docName = "document-name.webm"

  let botService = Mock<IBotService>()
  let resp = Mock<IResourceProvider>()
  let userConversionRepo = Mock<IUserConversionRepo>()
  let conversionRepo = Mock<IConversionRepo>()
  let createConversion: Create = fun _ -> Task.FromResult(conversion)

  do
    resp.Setup(fun r -> r[Resources.DocumentDownload, [| docName |]]).Returns(Resources.DocumentDownload)
    |> ignore

  do
    botService.Setup(_.ReplyToMessage(msgId, Resources.DocumentDownload)).ReturnsAsync(botMsgId)
    |> ignore

  let userConversion: UserConversion =
    { ChatId = chatId
      SentMessageId = botMsgId
      ReceivedMessageId = msgId
      ConversionId = conversionId }

  do
    userConversionRepo.Setup(_.SaveUserConversion(userConversion)).ReturnsAsync(())
    |> ignore

  do
    conversionRepo.Setup(_.QueuePreparation(conversionId, New.InputFile.Document { Id = docId; Name = docName })).ReturnsAsync(())
    |> ignore

  let handler =
    documentHandler createConversion userConversionRepo.Object conversionRepo.Object settings logger.Object botService.Object resp.Object

  [<Fact>]
  member _.``Sends message with document name if it's valid``() =
    let doc: Doc =
      { Id = docId
        Name = docName
        MimeType = "video/webm"
        Caption = None }

    let msg = { msg with Doc = Some doc }

    task {
      let! result = handler msg

      result |> should equal (Some())
      resp.VerifyAll()
      botService.VerifyAll()
      userConversionRepo.VerifyAll()
      conversionRepo.VerifyAll()
    }

  [<Fact>]
  member _.``Does nothing if document caption contains !nsfw``() =
    let doc: Doc =
      { Id = docId
        Name = docName
        MimeType = "video/webm"
        Caption = Some "!nsfw" }

    let msg = { msg with Doc = Some doc }

    task {
      let! result = handler msg

      result |> should equal None
      resp.VerifyNoOtherCalls()
      botService.VerifyNoOtherCalls()
    }

  [<Fact>]
  member _.``Does nothing if document mime type doesn't match to the settings``() =
    let doc: Doc =
      { Id = docId
        Name = docName
        MimeType = "video/mp4"
        Caption = None }

    let msg = { msg with Doc = Some doc }

    task {
      let! result = handler msg

      result |> should equal None
      resp.VerifyNoOtherCalls()
      botService.VerifyNoOtherCalls()
    }

  [<Fact>]
  member _.``Does nothing if message has no document``() =
    task {
      let! result = handler msg

      result |> should equal None
      resp.VerifyNoOtherCalls()
      botService.VerifyNoOtherCalls()
    }

type VidHandler() =
  let vidId = "vid-id"
  let vidName = "video-name.webm"

  let botService = Mock<IBotService>()
  let resp = Mock<IResourceProvider>()
  let userConversionRepo = Mock<IUserConversionRepo>()
  let conversionRepo = Mock<IConversionRepo>()
  let createConversion: Create = fun _ -> Task.FromResult({ Id = conversionId })

  do
    resp.Setup(fun r -> r[Resources.VideoDownload, [| vidName |]]).Returns(Resources.VideoDownload)
    |> ignore

  do
    botService.Setup(_.ReplyToMessage(msgId, Resources.VideoDownload)).ReturnsAsync(botMsgId)
    |> ignore

  let userConversion: UserConversion =
    { ChatId = chatId
      SentMessageId = botMsgId
      ReceivedMessageId = msgId
      ConversionId = conversionId }

  do
    userConversionRepo.Setup(_.SaveUserConversion(userConversion)).ReturnsAsync(())
    |> ignore

  do
    conversionRepo.Setup(_.QueuePreparation(conversionId, New.InputFile.Document { Id = vidId; Name = vidName })).ReturnsAsync(())
    |> ignore

  let handler =
    videoHandler createConversion userConversionRepo.Object conversionRepo.Object settings logger.Object botService.Object resp.Object

  [<Fact>]
  member _.``Sends message with video name if it's valid``() =
    let vid: Vid =
      { Id = vidId
        Name = Some vidName
        MimeType = "video/webm"
        Caption = None }

    let msg = { msg with Vid = Some vid }

    task {
      let! result = handler msg

      result |> should equal (Some())
      resp.VerifyAll()
      botService.VerifyAll()
      userConversionRepo.VerifyAll()
      conversionRepo.VerifyAll()
    }

  [<Fact>]
  member _.``Does nothing if video caption contains !nsfw``() =
    let vid: Vid =
      { Id = vidId
        Name = Some vidName
        MimeType = "video/webm"
        Caption = Some "!nsfw" }

    let msg = { msg with Vid = Some vid }

    task {
      let! result = handler msg

      result |> should equal None
      resp.VerifyNoOtherCalls()
      botService.VerifyNoOtherCalls()
    }

  [<Fact>]
  member _.``Does nothing if video mime type doesn't match to the settings``() =
    let vid: Vid =
      { Id = vidId
        Name = Some vidName
        MimeType = "video/mp4"
        Caption = None }

    let msg = { msg with Vid = Some vid }

    task {
      let! result = handler msg

      result |> should equal None
      resp.VerifyNoOtherCalls()
      botService.VerifyNoOtherCalls()
    }

  [<Fact>]
  member _.``Does nothing if message has no video``() =
    task {
      let! result = handler msg

      result |> should equal None
      resp.VerifyNoOtherCalls()
      botService.VerifyNoOtherCalls()
    }