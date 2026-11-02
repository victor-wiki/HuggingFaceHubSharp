namespace HuggingFaceHubSharp.Model
{
    public class DryRunFileInfo
    {
        public string commit_hash { get; set; }
        public long? file_size { get; set; }
        public string local_path { get; set; }
        public string filename { get; set; }
        public bool is_cached { get; set; }
        public bool will_download { get; set; }
    }
}
