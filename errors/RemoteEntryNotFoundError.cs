namespace HuggingFaceHubSharp.errors
{
    public class RemoteEntryNotFoundError: HfHubHTTPError, IEntryNotFoundError
    {
        public RemoteEntryNotFoundError(string message, HttpResponseMessage response, string server_message) : base(message, response, server_message)
        {
        }
    }
}
