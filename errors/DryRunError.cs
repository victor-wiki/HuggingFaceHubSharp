using PyException.common;

namespace HuggingFaceHubSharp.errors
{
    public class DryRunError:OSError
    {
        public DryRunError(string message) : base(message) { }
    }
}
