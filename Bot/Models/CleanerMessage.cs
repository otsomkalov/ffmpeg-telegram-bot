namespace Bot.Models
{
    public record CleanerMessage (string InputFilePath, string OutputFilePath, string ThumbnailFilePath)
    {
        public void Deconstruct(out string inputFilePath, out string outputFilePath, out string thumbnailFilePath)
        {
            inputFilePath = InputFilePath;
            outputFilePath = OutputFilePath;
            thumbnailFilePath = ThumbnailFilePath;
        }
    }
}
