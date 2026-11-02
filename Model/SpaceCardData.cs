namespace HuggingFaceHubSharp.Model
{
    /// <summary>
    /// Space Card Metadata that is used by Hugging Face Hub when included at the top of your README.md
    /// </summary>
    public class SpaceCardData
    {
        public string title { get; set; }
        public string sdk { get; set; }
        public string sdk_version { get; set; }
        public string python_version { get; set; }
        public string app_file { get; set; }
        public int? app_port { get; set; }
        public string license { get; set; }
        public string duplicated_from { get; set; }
        public List<string> models { get; set; }
        public List<string> datasets { get; set; }
        public List<string> tags { get; set; }
        public bool ignore_metadata_errors { get; set; }
    }
}
