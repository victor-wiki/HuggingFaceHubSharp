namespace HuggingFaceHubSharp.Model
{
    /// <summary>
    /// Contains information about a Space on the Hub. This object is returned by [`space_info`] and [`list_spaces`].
    /// </summary>
    public class SpaceInfo
    {
        public string id { get; set; }
        public string author { get; set; }
        public SpaceCardData card_data { get; set; }
        public DateTime? created_at { get; set; }
        public List<string> datasets { get; set; }
        public bool? disabled { get; set; }
        public dynamic gated { get; set; }
        public string host { get; set; }
        public DateTime? last_modified { get; set; }
        public int? likes { get; set; }
        public List<string> models { get; set; }
        public bool? @private { get; set; }
        public string region { get; set; }
        public Dictionary<string, dynamic> resource_group { get; set; }
        public SpaceRuntime runtime { get; set; }
        public string sdk { get; set; }
        public string sha { get; set; }
        public List<RepoSibling> siblings { get; set; }
        public string subdomain { get; set; }
        public List<string> tags { get; set; }
        public int? trending_score { get; set; }
        public int? used_storage { get; set; }
    }
}
