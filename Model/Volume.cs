namespace HuggingFaceHubSharp.Model
{
    public class Volume
    {
        public string type { get; set; }
        public string source { get; set; }
        public string mount_path { get; set; }
        public string revision { get; set; }
        public bool? read_only { get; set; }
        public string path { get; set; }
    }
}
