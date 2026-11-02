namespace HuggingFaceHubSharp.Model
{
    public class BlobSecurityInfo
    {
        /// <summary>
        /// duplicate information with "status" field, keeping it for backward compatibility
        /// </summary>
        public bool safe { get; set; }
        public string status { get; set; }
        public Dictionary<string, dynamic> av_scan { get; set; }
        public Dictionary<string, dynamic> pickle_import_scan { get; set; }
    }
}
