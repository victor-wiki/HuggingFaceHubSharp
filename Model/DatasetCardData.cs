namespace HuggingFaceHubSharp.Model
{
    public class DatasetCardData : CardData
    {
        public dynamic language { get; set; }
        public dynamic license { get; set; }
        public dynamic annotations_creators { get; set; }
        public dynamic language_creators { get; set; }
        public dynamic multilinguality { get; set; }
        public dynamic size_categories { get; set; }
        public List<string> source_datasets { get; set; }
        public dynamic task_categories { get; set; }
        public dynamic task_ids { get; set; }
        public string paperswithcode_id { get; set; }
        public string pretty_name { get; set; }
        public Dictionary<string, dynamic> train_eval_index { get; set; }
        public dynamic config_names { get; set; }
        public bool ignore_metadata_errors { get; set; }
    }
}
