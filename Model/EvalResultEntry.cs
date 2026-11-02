namespace HuggingFaceHubSharp.Model
{
    /// <summary>
    /// Evaluation result entry for the `.eval_results/*.yaml` format.

    /// Represents evaluation scores stored in model repos that automatically appear on
    /// the model page and the benchmark dataset's leaderboard.
    ///
    /// For the legacy `model-index` format in `README.md`, use[`EvalResult`] instead.
    ///
    /// See https://huggingface.co/docs/hub/eval-results for more details.
    /// </summary>
    public class EvalResultEntry
    {
        public string dataset_id { get; set; }
        public string task_id { get; set; }
        public dynamic value { get; set; }
        public string dataset_revision { get; set; }
        public string verify_token { get; set; }
        public string date { get; set; }
        public string source_url { get; set; }
        public string source_name { get; set; }
        public string source_user { get; set; }
        public string source_org { get; set; }
        public string notes { get; set; }
    }
}
