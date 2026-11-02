namespace HuggingFaceHubSharp.Model
{
    /// <summary>
    /// Parsed rate limit information from HTTP response headers.
    /// </summary>
    public class RateLimitInfo
    {
        public string resource_type { get; set; }
        public int remaining { get; set; }
        public int reset_in_seconds { get; set; }
        public int? limit { get; set; }
        public int? window_seconds { get; set; }
    }
}
