using PyException.common;

namespace HuggingFaceHubSharp.errors
{
    public class LocalTokenNotFoundError: EnvironmentError
    {
        public LocalTokenNotFoundError(string message) : base(message) { }
    }
}
