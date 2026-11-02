using SharpUtility.Extension;

namespace HuggingFaceHubSharp.Model
{
    /// <summary>
    /// Contains information about a folder on the Hub.
    /// </summary>
    public class RepoFolder: RepoInfo
    {
        public string tree_id { get; set; }

        public RepoFolder(Dictionary<string, dynamic> kwargs)
        {
            this.path = kwargs.pop("path");
            this.tree_id = kwargs.pop("oid");
            var last_commit = (kwargs.pop("lastCommit", null) ?? kwargs.pop("last_commit", null)) as Dictionary<string, dynamic>;

            if (last_commit != null)
            {
                this.last_commit = new LastCommitInfo()
                {
                    oid = last_commit.GetValueOrDefault("id"),
                    title = last_commit.GetValueOrDefault("title"),
                    date = DateTime.Parse(last_commit.GetValueOrDefault("date"))
                };
            }                         
        }
    }
}
