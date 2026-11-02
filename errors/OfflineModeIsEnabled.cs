using PyException.common;

namespace HuggingFaceHubSharp.errors
{
    public class OfflineModeIsEnabled : ConnectionError
    {
        public OfflineModeIsEnabled(string message) : base(message)
        {
        }
    }
}
