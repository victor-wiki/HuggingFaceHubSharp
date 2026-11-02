namespace HuggingFaceHubSharp.errors
{
    public class IncompleteSnapshotError : LocalEntryNotFoundError
    {
        public IncompleteSnapshotError(string message) : base(message)
        {
        }
    }
}
