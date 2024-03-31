namespace Infrastructure

open Domain.Core
open Domain.Workflows
open Infrastructure.Settings
open Infrastructure.Workflows
open Microsoft.Extensions.DependencyInjection
open MongoDB.Driver
open otsom.fs.Extensions.DependencyInjection
open Queue
open Domain.Repos

module Startup =
  let addDomain (services: IServiceCollection) =
    services
      .BuildSingleton<Conversion.Completed.Load, IMongoDatabase>(Conversion.Completed.load)
      .BuildSingleton<Conversion.Completed.DeleteVideo, WorkersSettings>(Conversion.Completed.deleteVideo)
      .BuildSingleton<Conversion.Completed.DeleteThumbnail, WorkersSettings>(Conversion.Completed.deleteThumbnail)
      .BuildSingleton<Conversion.Completed.Save, IMongoDatabase>(Conversion.Completed.save)
      .BuildSingleton<Conversion.Completed.QueueUpload, WorkersSettings>(Conversion.Completed.queueUpload)
      .BuildSingleton<Conversion.PreparedOrConverted.Load, IMongoDatabase>(Conversion.PreparedOrConverted.load)
      .BuildSingleton<Conversion.PreparedOrThumbnailed.Load, IMongoDatabase>(Conversion.PreparedOrThumbnailed.load)

      .BuildSingleton<Conversion.Thumbnailed.Complete, Conversion.Completed.Save>(Conversion.Thumbnailed.complete)
      .BuildSingleton<Conversion.Converted.Complete, Conversion.Completed.Save>(Conversion.Converted.complete)
