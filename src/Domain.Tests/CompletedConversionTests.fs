module Tests.Conversion.Completed

open System
open System.Threading.Tasks
open Domain.Core
open Domain.Core.Conversion
open Domain.Repos
open Domain.Workflows
open Xunit
open FsUnit.Xunit

[<Fact>]
let ``Completed conversion cleanup removes video and thumbnail``() =

  let video = Video("test-output-video.mp4")
  let thumbnail = Thumbnail("test-output-thumbnail.jpg")

  let conversion : Domain.Core.Conversion.Completed = {
    Id = Guid.NewGuid().ToString() |> ConversionId
    OutputFile = video
    ThumbnailFile = thumbnail
  }

  let deleteVideo: Conversion.Completed.DeleteVideo =
    fun vid ->
      vid |> should equal video

      Task.FromResult()

  let deleteThumbnail: Conversion.Completed.DeleteThumbnail =
    fun thumb ->
      thumb |> should equal thumbnail

      Task.FromResult()

  let sut = Conversion.Completed.cleanup deleteVideo deleteThumbnail

  sut conversion