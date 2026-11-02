namespace HuggingFaceHubSharp.Model
{
    /// <summary>
    /// Contains information about a kernel repo on the Hub. This object is returned by [`kernel_info`].
    /// </summary>
    public class KernelInfo
    {
        public string id { get; set; }
        public string author { get; set; }
        public int? downloads { get; set; }
        public dynamic gated { get; set; }
        public DateTime? last_modified { get; set; }
        public int? likes { get; set; }
        public bool @private { get; set; }
        public string sha { get; set; }
    }
}
