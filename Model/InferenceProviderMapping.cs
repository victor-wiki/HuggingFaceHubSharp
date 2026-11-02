namespace HuggingFaceHubSharp.Model
{
    public class InferenceProviderMapping
    {
        public string provider { get; set; }
        public string hf_model_id { get; set; }  // ID of the model on the Hugging Face Hub
        public string provider_id { get; set; }  // ID of the model on the provider's side
        public string status { get; set; }
        public string task { get; set; }

        public string adapter { get; set; }
        public string adapter_weights_path { get; set; }
        public string type { get; set; }
    }
}
