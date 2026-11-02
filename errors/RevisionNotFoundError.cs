namespace HuggingFaceHubSharp.errors
{
    public class RevisionNotFoundError : HfHubHTTPError
    {
        public RevisionNotFoundError(string message, HttpResponseMessage response, string server_message) : base(message, response, server_message)
        { 
        }
    }
}
