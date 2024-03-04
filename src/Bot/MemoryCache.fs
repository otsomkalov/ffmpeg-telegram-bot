module Bot.MemoryCache

open System.Collections.Generic
open System.Threading.Tasks

// [<RequireQualifiedAccess>]
// module Translation =
//
//   let getLocaleTranslations (getLocaleTranslations: Translation.GetLocaleTranslations) : Translation.GetLocaleTranslations =
//     let knownTranslations = Dictionary<_, _>()
//
//     fun lang ->
//       match knownTranslations.TryGetValue(lang) with
//       | true, t -> Task.FromResult t
//       | _ ->
//         task {
//           let! localeTranslations = getLocaleTranslations lang
//           knownTranslations.Add(lang, localeTranslations)
//           return localeTranslations
//         }
