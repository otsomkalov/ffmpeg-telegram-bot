module Domain.Startup

#nowarn "20"

open Domain.Core
open Domain.Workflows
open Microsoft.Extensions.DependencyInjection
open otsom.fs.Extensions.DependencyInjection

let addDomain (services: IServiceCollection) =
  services.BuildSingleton<Conversion.Create, IConversionRepo>(Conversion.create)

  services.AddSingleton<IConversionService, ConversionService>()