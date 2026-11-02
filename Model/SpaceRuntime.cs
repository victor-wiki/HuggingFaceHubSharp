namespace HuggingFaceHubSharp.Model
{
    /// <summary>
    /// Contains information about the current runtime of a Space.
    /// </summary>
    public class SpaceRuntime
    {
        public SpaceStage stage { get; set; }
        public SpaceHardware? hardware { get; set; }
        public SpaceHardware? requested_hardware { get; set; }
        public int? sleep_time { get; set; }
        public SpaceStorage? storage { get; set; }
        public bool dev_mode { get; set; }
        public SpaceHotReloading hot_reloading { get; set; }
        public List<Volume> volumes { get; set; }
        public Dictionary<string, dynamic> raw { get; set; }
    }
}
