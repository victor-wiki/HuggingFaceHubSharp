namespace HuggingFaceHubSharp.Model
{
    public class HfFileMetadata
    {
        public string commit_hash { get; set; }
        public string etag { get; set; }
        public string location { get; set; }
        public long? size { get; set; }
        public XetFileData xet_file_data { get; set; }
    }
}
