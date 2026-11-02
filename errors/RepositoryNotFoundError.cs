namespace HuggingFaceHubSharp.errors
{
    public class RepositoryNotFoundError : HfHubHTTPError
    {
        public RepositoryNotFoundError(string message, HttpResponseMessage response, string server_message) : base(message, response, server_message)
        {
        }
    }
}
