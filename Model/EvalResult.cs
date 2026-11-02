namespace HuggingFaceHubSharp.Model
{
    public class EvalResult
    {
        /// <summary>
        /// The task identifier
        /// 
        /// Example: automatic-speech-recognition
        /// </summary>
        public string task_type { get; set; }

        /// <summary>
        /// The dataset identifier
        /// 
        /// Example: common_voice.Use dataset id from https:hf.co/datasets
        /// </summary>
        public string dataset_type { get; set; }

        /// <summary>
        /// A pretty name for the dataset.
        /// 
        /// Example: Common Voice(French)
        /// </summary>
        public string dataset_name { get; set; }

        /// <summary>
        /// The metric identifier
        /// 
        /// Example: wer.Use metric id from https:hf.co/metrics
        /// </summary>
        public string metric_type { get; set; }


        /// <summary>
        /// Value of the metric.
        /// 
        /// Example: 20.0 or "20.0 ± 1.2"
        /// </summary>
        public dynamic metric_value { get; set; }


        /// <summary>
        /// A pretty name for the task.
        /// 
        /// Example: Speech Recognition
        /// </summary>
        public string task_name { get; set; }

        /// <summary>
        /// The name of the dataset configuration used in `load_dataset()`.
        /// 
        /// Example: fr in `load_dataset("common_voice", "fr")`.
        /// See the `datasets` docs for more info:
        /// https:huggingface.co/docs/datasets/package_reference/loading_methodsdatasets.load_dataset.name
        /// </summary>
        public string dataset_config { get; set; }

        /// <summary>
        /// The split used in `load_dataset()`.
        /// 
        /// Example: test
        /// </summary>
        public string dataset_split { get; set; }

        /// <summary>
        /// The revision(AKA Git Sha) of the dataset used in `load_dataset()`.
        /// 
        /// Example: 5503434ddd753f426f4b38109466949a1217c2bb
        /// </summary>
        public string dataset_revision { get; set; }

        /// <summary>
        /// The arguments passed during `Metric.compute()`.
        /// 
        /// Example for `bleu`: max_order: 4
        /// </summary>
        public Dictionary<string, dynamic> dataset_args { get; set; }

        /// <summary>
        ///  A pretty name for the metric.
        /// Example: Test WER
        /// </summary>
        public string metric_name { get; set; }

        /// <summary>
        /// The name of the metric configuration used in `load_metric()`. 
        /// 
        /// Example: bleurt-large-512 in `load_metric("bleurt", "bleurt-large-512")`.
        /// See the `datasets` docs for more info: https:huggingface.co/docs/datasets/v2.1.0/en/loadingload-configurations
        /// </summary>
        public string metric_config { get; set; }

        /// <summary>
        /// The arguments passed during `Metric.compute()`.
        /// 
        /// Example for `bleu`: max_order: 4
        /// </summary>
        public Dictionary<string, dynamic> metric_args { get; set; }

        /// <summary>
        /// Indicates whether the metrics originate from Hugging Face's [evaluation service](https:huggingface.co/spaces/autoevaluate/model-evaluator) or not. Automatically computed by Hugging Face, do not set.
        /// </summary>
        public bool? verified { get; set; }

        /// <summary>
        /// A JSON Web Token that is used to verify whether the metrics originate from Hugging Face's [evaluation service](https:huggingface.co/spaces/autoevaluate/model-evaluator) or not.
        /// </summary>
        public string verify_token { get; set; }

        /// <summary>
        /// The name of the source of the evaluation result.
        /// 
        /// Example: Open LLM Leaderboard
        /// </summary>
        public string source_name { get; set; }

        /// <summary>
        /// The URL of the source of the evaluation result.
        /// 
        /// Example: https:huggingface.co/spaces/open-llm-leaderboard/open_llm_leaderboard
        /// </summary>
        public string source_url { get; set; }
    }
}
