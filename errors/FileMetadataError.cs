using PyException.common;

namespace HuggingFaceHubSharp.errors
{
    public class FileMetadataError : OSError
    {
        public FileMetadataError(string message) : base(message)
        {
        }
    }
}
