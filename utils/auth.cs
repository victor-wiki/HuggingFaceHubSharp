using HuggingFaceHubSharp.errors;
using HuggingFaceHubSharp.Model;
using Newtonsoft.Json;
using SharpUtility.Extension;
using SharpUtility.Helper;
using System.Text;

namespace HuggingFaceHubSharp.utils
{
    public class auth
    {
        private static OAuthRefreshCache _OAUTH_REFRESH_CACHE;
        private static object _OAUTH_REFRESH_LOCK = new object();
        private const int _OAUTH_RECHECK_INTERVAL = 300; //re-check interval when there is no metadata or the refresh failed
        private const int _OAUTH_REFRESH_MARGIN = 24 * 3600;  // refresh when less than 1 day of validity remains
        private const string _REFRESH_TOKEN_GRANT_TYPE = "refresh_token";

        /// <summary>
        /// Get token if user is logged in.
        /// </summary>
        /// <returns></returns>
        public static async Task<string> get_token()
        {
            return _get_token_from_oidc() ?? _get_token_from_environment() ?? await _get_token_from_file_refreshed() ?? _get_token_from_google_colab();
        }

        /// <summary>
        /// Get a short-lived OIDC token in CI (Trusted Publishers).
        /// </summary>
        /// <returns></returns>
        private static string _get_token_from_oidc()
        {
            var resource = Environment.GetEnvironmentVariable("HF_OIDC_RESOURCE");

            if (resource == null)
                return null;

            ////to do
            return null;
        }

        private static string _get_token_from_environment()
        {
            // `HF_TOKEN` has priority (keep `HUGGING_FACE_HUB_TOKEN` for backward compatibility)
            return _clean_token(Environment.GetEnvironmentVariable("HF_TOKEN") ?? Environment.GetEnvironmentVariable("HUGGING_FACE_HUB_TOKEN"));
        }

        private static string _get_token_from_google_colab()
        {
            return null; ////to do
        }

        /// <summary>
        /// Get the token from `HF_TOKEN_PATH`, transparently refreshing it if close to expiry.
        /// </summary>
        /// <returns></returns>
        private static async Task<string> _get_token_from_file_refreshed()
        {
            var token = _get_token_from_file();

            if (token == null)
            {
                return null;
            }

            return await _refresh_oauth_token_if_needed(token);
        }

        /// <summary>
        /// Refresh an OAuth access token if it is close to expiry. Best-effort: never raises.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        private static async Task<string> _refresh_oauth_token_if_needed(string token)
        {
            //lock (_OAUTH_REFRESH_LOCK)
            {
                var now = DateTime.UtcNow;
                var totalSeconds = now.GetTotalSeconds();
                var cache = _OAUTH_REFRESH_CACHE;

                if (cache != null && cache.GetValueOrDefault("file_token", null) == token && now < cache["recheck_at"]) ////to do
                {
                    return cache["resolved_token"];
                }

                var tokens = _read_stored_tokens_full();

                var hf_token = tokens.FirstOrDefault(item => item.Value.GetValueOrDefault("hf_token") == token);

                var token_name = hf_token.Key;
                var fields = hf_token.Value ?? new Dictionary<string, string>();

                var refresh_token = fields.GetValueOrDefault("refresh_token");
                var expires_at = _parse_expires_at(fields);

                if (token_name == null || refresh_token == null || expires_at == null)
                {
                    // `token` may have just been replaced by a concurrent refresh (in which case it no
                    // longer appears in the stored tokens): serve the fresh file token without caching.
                    var current_file_token = _get_token_from_file();

                    if (current_file_token != null && current_file_token != token)
                    {
                        return current_file_token;
                    }

                    // Not a refreshable OAuth token (or its metadata was lost): nothing to do.
                    _OAUTH_REFRESH_CACHE = new OAuthRefreshCache()
                    {
                        { "file_token", token },
                        { "resolved_token", token },
                        { "recheck_at", now.AddSeconds(_OAUTH_RECHECK_INTERVAL) }
                    };

                    return token;
                }

                if ((expires_at - _OAUTH_REFRESH_MARGIN) > totalSeconds) ////to do
                {
                    _OAUTH_REFRESH_CACHE = new OAuthRefreshCache()
                    {
                        { "file_token", token },
                        { "resolved_token", token },
                        { "recheck_at", expires_at.Value - _OAUTH_REFRESH_MARGIN }
                    };

                    return token;
                }

                string new_token = null;
                long? new_expires_at = null;

                try
                {
                    fields = _read_stored_tokens_full().GetValueOrDefault(token_name, new Dictionary<string, string>());

                    if (fields.GetValueOrDefault("hf_token") != token)
                    {
                        // Another process already refreshed this token: adopt its result.
                        new_token = fields.GetValueOrDefault("hf_token", token);
                        new_expires_at = _parse_expires_at(fields);
                    }
                    else
                    {
                        var response = await refresh_access_token(refresh_token);
                        new_token = response.GetValueOrDefault("access_token");
                        new_expires_at = response.ContainsKey("expires_in") ? (totalSeconds + (response.GetValueOrDefault("expires_in"))) : null;

                        _save_token(
                            token: new_token,
                            token_name: token_name,
                            // The server may rotate the refresh token; keep the old one if it doesn't.
                            refresh_token: response.GetValueOrDefault("refresh_token", refresh_token),
                            expires_at: new_expires_at
                        );

                        // Update the active token file, unless another process switched to a different token meanwhile.
                        if (_get_token_from_file() == token)
                        {
                            File.WriteAllText(constants.HF_TOKEN_PATH, new_token);
                        }

                        LogHelper.LogInfo($"Access token `{token_name}` has been refreshed.");
                    }
                }
                catch (Exception ex)
                {
                    var recheck_at = totalSeconds + _OAUTH_RECHECK_INTERVAL;
                    _OAUTH_REFRESH_CACHE = new OAuthRefreshCache() { { "file_token", token }, { "resolved_token", token }, { "recheck_at", recheck_at } };

                    return token;
                }

                _OAUTH_REFRESH_CACHE = new OAuthRefreshCache()
                {
                    { "file_token", new_token },
                    { "resolved_token", new_token },
                    // The floor guards against a token lifetime shorter than the refresh margin, which
                    // would otherwise put `recheck_at` in the past and trigger a refresh on every call.
                    { "recheck_at", Math.Max( totalSeconds + (long)_OAUTH_RECHECK_INTERVAL,  new_expires_at.HasValue? new_expires_at.Value - _OAUTH_REFRESH_MARGIN:0) }
                };

                return new_token;
            }
        }

        private static void _save_token(string token, string token_name, string refresh_token = null, long? expires_at = null)
        {
            var stored_tokens = _read_stored_tokens_full();

            var fields = new Dictionary<string, string>() { { "hf_token", token } };

            if (refresh_token != null)
            {
                fields["refresh_token"] = refresh_token;
            }

            if (expires_at != null)
            {
                fields["expires_at"] = expires_at.ToString();
            }

            // Replace the whole section: re-logging in under the same name must drop stale metadata.
            stored_tokens[token_name] = fields;
            _save_stored_tokens_full(stored_tokens);

            LogHelper.LogInfo($"The token `{token_name}` has been saved to {constants.HF_STORED_TOKENS_PATH}");
        }

        /// <summary>
        /// Write all sections and their fields to the stored tokens INI file.
        /// </summary>
        /// <param name="stored_tokens"></param>
        private static void _save_stored_tokens_full(Dictionary<string, Dictionary<string, string>> stored_tokens)
        {
            var sorted = stored_tokens.OrderBy(item => item.Key);

            File.WriteAllText(constants.HF_STORED_TOKENS_PATH, JsonConvert.SerializeObject(sorted));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="refresh_token"></param>
        /// <returns>the full token response: `access_token`, and optionally a rotated `refresh_token` and `expires_in`.</returns>
        private static async Task<OAuthTokenResponse> refresh_access_token(string refresh_token)
        {
            string url = $"{constants.ENDPOINT}/oauth/token";

            using var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(constants.HF_HUB_DOWNLOAD_TIMEOUT);

            var json = JsonConvert.SerializeObject(new Dictionary<string, dynamic>()
            {
                {"grant_type" , _REFRESH_TOKEN_GRANT_TYPE },
                { "refresh_token", refresh_token },
                { "client_id", constants.DEVICE_CODE_OAUTH_CLIENT_ID }
            });

            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);

            var data = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(await response.Content.ReadAsStringAsync());

            if (data.ContainsKey("access_token"))
            {
                return data as OAuthTokenResponse; //to do
            }

            var error = data.GetValueOrDefault("error");

            if (error != null)
            {
                throw new DeviceCodeError($"Failed to refresh access token: {error ?? response.StatusCode} - {data.GetValueOrDefault("error_description", "")}");
            }

            return null;
        }

        /// <summary>
        /// Parse the `expires_at` field of a stored-tokens section, `null` if missing or corrupt.
        /// </summary>
        /// <param name="dict"></param>
        /// <returns></returns>
        private static long? _parse_expires_at(Dictionary<string, string> fields)
        {
            try
            {
                return long.Parse(fields.GetValueOrDefault("expires_at"));
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        /// <summary>
        /// Read all sections of the stored tokens INI file, with all their fields.
        /// </summary>
        /// <returns></returns>
        private static Dictionary<string, Dictionary<string, string>> _read_stored_tokens_full()
        {
            var tokens_path = constants.HF_STORED_TOKENS_PATH;

            if (!File.Exists(tokens_path))
            {
                return new Dictionary<string, Dictionary<string, string>>();
            }

            // interpolation=null: token values are opaque strings, a `%` must not be interpreted.
            string content = File.ReadAllText(tokens_path);

            try
            {
                return JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, string>>>(content);
            }
            catch (Exception ex)
            {
                return new Dictionary<string, Dictionary<string, string>>();
            }
        }

        private static string _get_token_from_file()
        {
            try
            {
                if (!File.Exists(constants.HF_TOKEN_PATH))
                {
                    return null;
                }

                return _clean_token(File.ReadAllText(constants.HF_TOKEN_PATH));
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        /// <summary>
        /// Clean token by removing trailing and leading spaces and newlines.
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        private static string _clean_token(string token)
        {
            if (token == null)
            {
                return null;
            }

            return token.Replace("\r", "").Replace("\n", "").Trim();
        }
    }
}
