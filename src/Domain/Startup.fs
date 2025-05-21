module Domain.Startup

#nowarn "20"

open Microsoft.Extensions.DependencyInjection

let addDomain (services: IServiceCollection) =
  services.AddSingleton<IConversionService, ConversionService>()
