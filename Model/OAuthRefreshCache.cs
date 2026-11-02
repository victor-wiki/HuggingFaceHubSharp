namespace HuggingFaceHubSharp.Model
{
    public class OAuthRefreshCache: Dictionary<string, dynamic>
    {
        public string file_token { get; set; }  // token as read from HF_TOKEN_PATH (cache key)
        public string resolved_token { get; set; }  // token to return (refreshed, or identical if no refresh was needed)
        public float recheck_at { get; set; }  // wall-clock timestamp after which the expiry must be re-evaluated
    }
}
