namespace HuggingFaceHubSharp.errors
{
    public class GatedRepoError : RepositoryNotFoundError
    {
        public GatedRepoError(string message, HttpResponseMessage response, string server_message) : base(message, response, server_message)
        {
        }
    }
}
