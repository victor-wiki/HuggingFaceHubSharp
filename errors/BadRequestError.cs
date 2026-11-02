namespace HuggingFaceHubSharp.errors
{
    public class BadRequestError : HfHubHTTPError
    {
        public BadRequestError(string message, HttpResponseMessage response, string server_message) : base(message, response, server_message)
        {
        }
    }
}
