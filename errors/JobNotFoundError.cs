namespace HuggingFaceHubSharp.errors
{
    public class JobNotFoundError : HfHubHTTPError
    {
        public JobNotFoundError(string message, HttpResponseMessage response, string server_message) : base(message, response, server_message)
        {
        }
    }
}
