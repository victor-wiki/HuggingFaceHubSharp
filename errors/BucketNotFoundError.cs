namespace HuggingFaceHubSharp.errors
{
    public class BucketNotFoundError : HfHubHTTPError
    {
        public BucketNotFoundError(string message, HttpResponseMessage response, string server_message) : base(message, response, server_message)
        {
        }
    }
}
