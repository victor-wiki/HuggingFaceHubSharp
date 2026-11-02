using SharpUtility.Extension;

namespace HuggingFaceHubSharp.Model
{
    /// <summary>
    /// Contains information about a file on the Hub.
    /// </summary>
    public class RepoFile : RepoInfo
    {
        public long size { get; set; }
        public string blob_id { get; set; }
        public BlobLfsInfo lfs { get; set; }
        public string xet_hash { get; set; }
        public BlobSecurityInfo security { get; set; }
        public string rfilename { get; set; }
        public LastCommitInfo lastCommit { get; set; }

        public RepoFile(Dictionary<string, dynamic> kwargs)
        {
            this.path = kwargs.pop("path");
            this.size = kwargs.pop("size");
            this.blob_id = kwargs.pop("oid");
            var lfs_dict = kwargs.pop("lfs", null) as Dictionary<string, dynamic>;

            if (lfs_dict != null)
            {
                this.lfs = new BlobLfsInfo() { size = lfs_dict.GetValueOrDefault("size"), sha256 = lfs_dict.GetValueOrDefault("oid"), pointer_size = lfs_dict.GetValueOrDefault("pointerSize") };
            }

            this.xet_hash = kwargs.pop("xetHash", null);

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

            var security = kwargs.pop("securityFileStatus", null) as Dictionary<string, dynamic>;

            if (security != null)
            {
                var safe = security.GetValueOrDefault("status") == "safe";
                this.security = new BlobSecurityInfo()
                {
                    safe = safe,
                    status = security.GetValueOrDefault("status"),
                    av_scan = security.GetValueOrDefault("avScan"),
                    pickle_import_scan = security.GetValueOrDefault("pickleImportScan")
                };                 
            }

            // backwards compatibility
            this.rfilename = this.path;
            this.lastCommit = this.last_commit;
        }
    }
}
