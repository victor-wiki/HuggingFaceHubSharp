namespace HuggingFaceHubSharp.Model
{
    public class SpaceHotReloading
    {
        public string status { get; set; }
        public List<(string, string)> replica_statuses { get; set; }// See _hot_reloading_types.ApiCreateReloadResponse.res.status
        public Dictionary<string, dynamic> raw { get; set; }
    }
}
