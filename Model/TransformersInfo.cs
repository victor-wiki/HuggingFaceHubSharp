namespace HuggingFaceHubSharp.Model
{
    public class TransformersInfo: Dictionary<string, dynamic>
    {
        public string auto_model { get; set; }
        public string custom_class { get; set; }
        /// <summary>
        ///  possible `pipeline_tag` values: https://github.com/huggingface/huggingface.js/blob/3ee32554b8620644a6287e786b2a83bf5caf559c/packages/tasks/src/pipelines.ts#L72
        /// </summary>
        public string pipeline_tag { get; set; }
        public string processor { get; set; }
    }
}
