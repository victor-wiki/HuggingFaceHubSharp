namespace HuggingFaceHubSharp.Model
{
    public class ModelCardData : CardData
    {
        public dynamic base_model { get; set; }
        public dynamic datasets { get; set; }
        public List<EvalResult> eval_results { get; set; }
        public dynamic language { get; set; }
        public string library_name { get; set; }
        public string license { get; set; }
        public string license_name { get; set; }
        public string license_link { get; set; }
        public List<string> metrics { get; set; }
        public string model_name { get; set; }
        public string pipeline_tag { get; set; }
        public List<string> tags { get; set; }
        public bool ignore_metadata_errors { get; set; }
    }
}
