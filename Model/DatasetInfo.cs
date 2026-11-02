namespace HuggingFaceHubSharp.Model
{
    public class DatasetInfo
    {
        public string id { get; set; }
        public string author { get; set; }
        public DatasetCardData card_data { get; set; }
        public string citation { get; set; }
        public DateTime? created_at { get; set; }
        public string description { get; set; }
        public bool? disabled { get; set; }
        public int? downloads { get; set; }
        public int? downloads_all_time { get; set; }
        public dynamic gated { get; set; }
        public DateTime? last_modified { get; set; }
        public int? likes { get; set; }
        public int? main_size { get; set; }
        public string paperswithcode_id { get; set; }
        public bool? @private { get; set; }
        public Dictionary<string, dynamic> resource_group { get; set; }
        public string sha { get; set; }
        public List<RepoSibling> siblings { get; set; }
        public List<string> tags { get; set; }
        public int? trending_score { get; set; }
        public int? used_storage { get; set; }
    }
}
