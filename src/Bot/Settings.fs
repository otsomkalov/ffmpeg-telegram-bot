module Bot.Settings

[<CLIMutable>]
type InputValidationSettings =
  {
    LinkRegex: string
    MimeTypes: string seq
  }

  static member SectionName = "Validation"