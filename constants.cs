namespace HuggingFaceHubSharp
{
    public class constants
    {
        private static readonly string _HF_DEFAULT_ENDPOINT = "https://huggingface.co";
        private static readonly string _HF_DEFAULT_STAGING_ENDPOINT = "https://hub-ci.huggingface.co";
        private static string default_cache_path => Path.Combine(HF_HOME, "hub");
        private static bool _staging_mode => is_true(Environment.GetEnvironmentVariable("HUGGINGFACE_CO_STAGING"));
        private static string default_home => Path.Combine(USER_PROFILE_FOLDER, ".cache");
        private static string default_xet_cache_path => Path.Combine(HF_HOME, "xet");
        private static string _staging_home => Path.Combine(default_home, "huggingface_staging");

        public static string HUGGINGFACE_CO_URL_HOME = "https://huggingface.co/";
        public static bool HF_HUB_OFFLINE => is_true(Environment.GetEnvironmentVariable("HF_HUB_OFFLINE") ?? Environment.GetEnvironmentVariable("TRANSFORMERS_OFFLINE"));

        public const string PYTORCH_WEIGHTS_NAME = "pytorch_model.bin";
        public const string TF2_WEIGHTS_NAME = "tf_model.h5";
        public const string TF_WEIGHTS_NAME = "model.ckpt";
        public const string FLAX_WEIGHTS_NAME = "flax_model.msgpack";
        public const string CONFIG_NAME = "config.json";
        public const string REPOCARD_NAME = "README.md";
        public const string EVAL_RESULTS_FOLDER = ".eval_results";
        public const int DEFAULT_ETAG_TIMEOUT = 10;
        public const int DEFAULT_DOWNLOAD_TIMEOUT = 10;
        public const int DEFAULT_REQUEST_TIMEOUT = 10;
        public const int DOWNLOAD_CHUNK_SIZE = 10 * 1024 * 1024;
        public const long MAX_HTTP_DOWNLOAD_SIZE = 50l * 1000 * 1000 * 1000;  // 50 GB      

        public static string USER_PROFILE_FOLDER => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        public static string HF_HOME => Environment.ExpandEnvironmentVariables(Environment.GetEnvironmentVariable("HF_HOME") ?? Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? default_home, "huggingface"));
        public static string HUGGINGFACE_HUB_CACHE => Environment.GetEnvironmentVariable("HUGGINGFACE_HUB_CACHE") ?? default_cache_path;
        public static string HF_HUB_CACHE => Environment.ExpandEnvironmentVariables(Environment.GetEnvironmentVariable("HF_HUB_CACHE‌") ?? HUGGINGFACE_HUB_CACHE);

        public const string DEFAULT_REVISION = "main";
        public const string REPO_TYPE_DATASET = "dataset";
        public const string REPO_TYPE_SPACE = "space";
        public const string REPO_TYPE_MODEL = "model";
        public const string REPO_TYPE_KERNEL = "kernel";
        public static readonly string[] REPO_TYPES = [null, REPO_TYPE_MODEL, REPO_TYPE_DATASET, REPO_TYPE_SPACE];
        public static readonly string[] REPO_TYPES_WITH_KERNEL = Enumerable.Concat(REPO_TYPES, [REPO_TYPE_KERNEL]).ToArray();
        public static readonly string[] SPACES_SDK_TYPES = ["gradio", "streamlit", "docker", "static"];
        public const string REPO_ID_SEPARATOR = "--";
        public static readonly string[] ENV_VARS_TRUE_VALUES = ["1", "ON", "YES", "TRUE"];
        public static bool HF_HUB_DISABLE_TELEMETRY => is_true(Environment.GetEnvironmentVariable("HF_HUB_DISABLE_TELEMETRY"))  // HF-specific env variable
            || is_true(Environment.GetEnvironmentVariable("DISABLE_TELEMETRY"))
            || is_true(Environment.GetEnvironmentVariable("DO_NOT_TRACK"));  // https://donottrack.sh/

        // Used to override the etag timeout on a system level
        public static int HF_HUB_ETAG_TIMEOUT => int.Parse(Environment.GetEnvironmentVariable("HF_HUB_ETAG_TIMEOUT") ?? DEFAULT_ETAG_TIMEOUT.ToString());

        public static string HF_TOKEN_PATH => Path.Combine(_staging_home, "token");
        public static string HF_STORED_TOKENS_PATH => Path.Combine(Path.GetDirectoryName(HF_TOKEN_PATH), "stored_tokens");

        public static string ENDPOINT => _staging_mode ? _HF_DEFAULT_STAGING_ENDPOINT : (Environment.GetEnvironmentVariable("HF_ENDPOINT") ?? _HF_DEFAULT_ENDPOINT).TrimEnd('/');
        public static string DEVICE_CODE_OAUTH_CLIENT_ID => Environment.GetEnvironmentVariable("HF_DEVICE_CODE_OAUTH_CLIENT_ID") ?? "26be6b09-91c5-47da-9861-d2d2bb7a7e36";
        public static int HF_HUB_DOWNLOAD_TIMEOUT => int.Parse(Environment.GetEnvironmentVariable("HF_HUB_DOWNLOAD_TIMEOUT") ?? DEFAULT_DOWNLOAD_TIMEOUT.ToString());

        // Disable sending the cached token by default is all HTTP requests to the Hub
        public static bool HF_HUB_DISABLE_IMPLICIT_TOKEN => is_true(Environment.GetEnvironmentVariable("HF_HUB_DISABLE_IMPLICIT_TOKEN"));

        // Allows to add information about the requester in the user-agent (e.g. partner name)
        public static string HF_HUB_USER_AGENT_ORIGIN => Environment.GetEnvironmentVariable("HF_HUB_USER_AGENT_ORIGIN");

        public static string HUGGINGFACE_CO_URL_TEMPLATE => (_staging_mode ? _HF_DEFAULT_STAGING_ENDPOINT : ENDPOINT) + "/{repo_id}/resolve/{revision}/{filename}";

        public static readonly Dictionary<string, string> REPO_TYPES_URL_PREFIXES = new Dictionary<string, string>()
        {
           { REPO_TYPE_DATASET, "datasets/" },
           { REPO_TYPE_SPACE, "spaces/" },
           { REPO_TYPE_KERNEL, "kernels/" }
        };

        public const string HUGGINGFACE_HEADER_X_REPO_COMMIT = "X-Repo-Commit";
        public const string HUGGINGFACE_HEADER_X_LINKED_ETAG = "X-Linked-Etag";
        public const string HUGGINGFACE_HEADER_X_LINKED_SIZE = "X-Linked-Size";
        public const string HUGGINGFACE_HEADER_X_BILL_TO = "X-HF-Bill-To";

        public const string HUGGINGFACE_HEADER_X_XET_ENDPOINT = "X-Xet-Cas-Url";
        public const string HUGGINGFACE_HEADER_X_XET_ACCESS_TOKEN = "X-Xet-Access-Token";
        public const string HUGGINGFACE_HEADER_X_XET_EXPIRATION = "X-Xet-Token-Expiration";
        public const string HUGGINGFACE_HEADER_X_XET_HASH = "X-Xet-Hash";
        public const string HUGGINGFACE_HEADER_X_XET_REFRESH_ROUTE = "X-Xet-Refresh-Route";
        public const string HUGGINGFACE_HEADER_LINK_XET_AUTH_KEY = "xet-auth";
        public static string HF_XET_CACHE => Environment.GetEnvironmentVariable("HF_XET_CACHE") ?? default_xet_cache_path;
        public static bool HF_HUB_DISABLE_XET => is_true(Environment.GetEnvironmentVariable("HF_HUB_DISABLE_XET"));
        // Disable symlinks in the cache (files are copied instead of symlinked)
        public static bool HF_HUB_DISABLE_SYMLINKS => is_true(Environment.GetEnvironmentVariable("HF_HUB_DISABLE_SYMLINKS"));

        // Disable the cache-wide shared blob store (see `huggingface_hub.utils._shared_blobs`).
        public static bool HF_HUB_DISABLE_SHARED_BLOBS => is_true(Environment.GetEnvironmentVariable("HF_HUB_DISABLE_SHARED_BLOBS"));
        // Disable warning on machines that do not support symlinks (e.g. Windows non-developer)
        public static bool HF_HUB_DISABLE_SYMLINKS_WARNING => is_true(Environment.GetEnvironmentVariable("HF_HUB_DISABLE_SYMLINKS_WARNING"));
        public static readonly string[] DISCUSSION_TYPES = ["all", "discussion", "pull_request"];
        public static readonly string[] DISCUSSION_STATUS = ["all", "open", "closed"];
        public static bool SHOW_DOWNLOAD_PROGRESS_BAR= is_true(Environment.GetEnvironmentVariable("SHOW_DOWNLOAD_PROGRESS_BAR"));

        public static readonly Dictionary<string, string> REPO_TYPES_MAPPING = new Dictionary<string, string>()
        {
            {"datasets", REPO_TYPE_DATASET },
            {"spaces", REPO_TYPE_SPACE },
            {"models", REPO_TYPE_MODEL },
            {"kernels", REPO_TYPE_KERNEL }
        };

        public static List<string> HF_URL_HOSTS
        {
            get
            {
                return new List<string>()
                {
                    "hf.co",
                    new Uri(_HF_DEFAULT_ENDPOINT).Host,
                    new Uri(_HF_DEFAULT_STAGING_ENDPOINT).Host,
                    new Uri(ENDPOINT).Host
                };
            }
        }

        public static bool is_true(string value)
        {
            if (value == null)
            {
                return false;
            }

            return ENV_VARS_TRUE_VALUES.Contains(value.ToUpper());
        }

        /// <summary>
        /// whether we are in offline mode for the Hub.
        /// When offline mode is enabled, all HTTP requests made with `get_session` will raise an `OfflineModeIsEnabled` exception.
        /// </summary>
        /// <returns></returns>
        public static bool is_offline_mode()
        {
            return HF_HUB_OFFLINE;
        }

        /// <summary>
        /// Return whether *path*'s filename identifies it as a Mistral tekken vocabulary file.
        /// 
        /// Mirrors the classification rule of `mistral_common.tokens.tokenizers.tekken.is_tekken`:
        /// the basename must contain `"tekken"` and end with `.json`.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static bool is_tekken_vocab_filename(string path)
        {
            var basename = Path.GetFileName(path);

            return basename.Contains("tekken") && basename.EndsWith(".json");
        }
    }
}
