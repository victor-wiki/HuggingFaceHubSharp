namespace HuggingFaceHubSharp.Model
{
    /// <summary>
    /// Contains information about a git commit for a repo on the Hub. Check out [`list_repo_commits`] for more details.
    /// </summary>
    public class GitCommitInfo
    {
        public string commit_id { get; set; }

        public List<string> authors { get; set; }
        public DateTime created_at { get; set; }
        public string title { get; set; }
        public string message { get; set; }

        public string formatted_title { get; set; }
        public string formatted_message { get; set; }
    }
}
