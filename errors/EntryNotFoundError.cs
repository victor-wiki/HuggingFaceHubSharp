namespace HuggingFaceHubSharp.errors
{
    public class EntryNotFoundError : Exception, IEntryNotFoundError
    {
        public EntryNotFoundError()
        {
        }

        public EntryNotFoundError(string? message) : base(message)
        {
        }
    }
}
