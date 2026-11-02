namespace HuggingFaceHubSharp.Model
{
    /// <summary>
    /// Contains information about a model on the Hub. This object is returned by [`model_info`] and [`list_models`].
    /// </summary>
    public class ModelInfo
    {
        public string id { get; set; }
        public string author { get; set; }
        public List<string> base_models { get; set; }
        public ModelCardData card_data { get; set; }
        public int children_model_count { get; set; }
        public Dictionary<string, dynamic> config { get; set; }
        public DateTime? created_at { get; set; }
        public bool? disabled { get; set; }
        public int? downloads { get; set; }
        public int? downloads_all_time { get; set; }
        public List<EvalResultEntry> eval_results { get; set; }
        public dynamic gated { get; set; }
        public Dictionary<string, dynamic> gguf { get; set; }
        public string inference { get; set; }
        public List<InferenceProviderMapping> inference_provider_mapping { get; set; }
        public DateTime? last_modified { get; set; }
        public string library_name { get; set; }
        public int? likes { get; set; }
        public string mask_token { get; set; }
        public Dictionary<string, dynamic> model_index { get; set; }
        public string pipeline_tag { get; set; }
        public bool? @private { get; set; }
        public Dictionary<string, dynamic> resource_group { get; set; }
        public SafeTensorsInfo safetensors { get; set; }
        public Dictionary<string, dynamic> security_repo_status { get; set; }
        public string sha { get; set; }
        public List<RepoSibling> siblings { get; set; }
        public List<string> spaces { get; set; }
        public List<string> tags { get; set; }
        public TransformersInfo transformers_info { get; set; }
        public int? trending_score { get; set; }
        public int? used_storage { get; set; }
        public dynamic widget_data { get; set; }
    }
}
