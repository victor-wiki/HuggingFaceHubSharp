namespace HuggingFaceHubSharp.errors
{
    public class LocalEntryNotFoundError: FileNotFoundException
    {
        public LocalEntryNotFoundError(string message):base(message) { }
    }
}
