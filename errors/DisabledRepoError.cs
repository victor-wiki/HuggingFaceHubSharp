namespace HuggingFaceHubSharp.errors
{
    public class DisabledRepoError : HfHubHTTPError
    {
        public DisabledRepoError(string message, HttpResponseMessage response, string server_message) : base(message, response, server_message)
        {
        }
    }
}
