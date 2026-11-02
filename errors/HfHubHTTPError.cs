using PyException.httpx;

namespace HuggingFaceHubSharp.errors
{
    public class HfHubHTTPError: HTTPError
    {
        public string repo_type { get; set; }
        public string repo_id { get; set; }
        public string bucket_id { get; set; }
        public string job_id { get; set; }
        public HttpResponseMessage Response { get; protected set; }
        public string ServerMessage { get; protected set; }   

        public HfHubHTTPError(string message, HttpResponseMessage response, string server_message) : base(message) 
        {
            this.Response = response;
            this.ServerMessage = server_message;
        }
    }
}
