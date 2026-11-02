namespace HuggingFaceHubSharp.Model
{
    /// <summary>
    /// A Discussion or Pull Request on the Hub.
    /// </summary>
    public class Discussion
    {
        public string title { get; set; }
        public string status { get; set; }
        public int num { get; set; }
        public string repo_id { get; set; }
        public string repo_type { get; set; }
        public string author { get; set; }
        public bool is_pull_request { get; set; }
        public DateTime created_at { get; set; }
        public string endpoint { get; set; }

        /// <summary>
        /// If this is a Pull Request , returns the git reference to which changes can be pushed.
        /// Returns `None` otherwise.
        /// </summary>
        public string git_reference
        {
            get
            {
                if(this.is_pull_request)
                {
                    return $"refs/pr/{this.num}";
                }

                return null;
            }
        }
    }
}
