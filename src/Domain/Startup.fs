module Domain.Startup

open Microsoft.Extensions.DependencyInjection

let addDomain (services: IServiceCollection) =
  services.AddSingleton<IConversionService, ConversionService>()
