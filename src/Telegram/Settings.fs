module Telegram.Settings

[<CLIMutable>]
type InputValidationSettings =
  { LinkRegex: string
    MimeTypes: string seq }

  static member SectionName = "Validation"