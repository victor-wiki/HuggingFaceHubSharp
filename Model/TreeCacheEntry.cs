namespace HuggingFaceHubSharp.Model
{
    /// <summary>
    /// Raw metadata of a single file in a cached tree listing, mirroring the `/tree` endpoint fields.
    /// </summary>
    public class TreeCacheEntry
    {
        public long size { get; set; }
        public string blob_id { get; set; }
        public string lfs_sha256 { get; set; }
        public long? lfs_size { get; set; }
        public string xet_hash { get; set; }
    }
}
