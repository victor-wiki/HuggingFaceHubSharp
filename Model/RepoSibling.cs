namespace HuggingFaceHubSharp.Model
{
    /// <summary>
    /// Contains basic information about a repo file inside a repo on the Hub.
    /// </summary>
    public class RepoSibling
    {
        public string rfilename { get; set; }
        public long? size { get; set; }
        public string blob_id { get; set; }
        public BlobLfsInfo lfs { get; set; }
    }
}
