using HuggingFaceHubSharp.errors;
using SharpUtility.Extension;

namespace HuggingFaceHubSharp.utils
{
    public class headers
    {
        /// <summary>
        /// Build headers dictionary to send in a HF Hub call.
        /// </summary>
        /// <param name="token"></param>
        /// <param name="library_name"></param>
        /// <param name="library_version"></param>
        /// <param name="user_agent"></param>
        /// <param name="headers"></param>
        /// <returns>A `dict` of headers to pass in your API call.</returns>
        public static async Task<Dictionary<string, string>> build_hf_headers(dynamic token = null,
                string library_name = null,
                string library_version = null,
                dynamic user_agent = null,
                Dictionary<string, string> headers = null)
        {
            // Get auth token to send
            var token_to_send = await get_token_to_send(token);

            // Combine headers
            var hf_headers = new Dictionary<string, string>()
                {
                    {"user-agent", _http_user_agent(library_name : library_name, library_version : library_version, user_agent : user_agent) }
                };

            if (token_to_send != null)
            {
                hf_headers["authorization"] = $"Bearer {token_to_send}";
            }

            if (headers != null)
            {
                hf_headers.update(headers);
            }

            return hf_headers;
        }

        /// <summary>
        /// Format a user-agent string containing information about the installed packages.
        /// </summary>
        /// <param name="library_name">The name of the library that is making the HTTP request.</param>
        /// <param name="library_version">The version of the library that is making the HTTP request.</param>
        /// <param name="user_agent">The user agent info in the form of a dictionary or a single string.</param>
        /// <returns>The formatted user-agent string.</returns>
        private static string _http_user_agent(string library_name = null, string library_version = null, dynamic user_agent = null)
        {
            string ua = null;

            if (library_name != null)
            {
                ua = $"{library_name}/{library_version}";
            }
            else
            {
                ua = "unknown/null";
            }

            ua += $"; hf_hub/{Initor.VERSION}";
            ua += $"; python/{Initor.PYTHON_VERSION}";

            if (!constants.HF_HUB_DISABLE_TELEMETRY)
            {
                ua += $"; torch/{Initor.TORCH_VERSION}";

                var agent = detect_agent.detect_agent_value();

                if (agent != null)
                {
                    ua += $"; agent/{agent}";
                }
            }

            if (user_agent is Dictionary<string, dynamic> dict) ////to do
            {
                ua += "; " + string.Join("; ", dict.Select(item => $"{item.Key}/{item.Value}"));
            }
            else if (user_agent is string str)
            {
                ua += "; " + str;
            }

            // Retrieve user-agent origin headers from environment variable
            var origin = constants.HF_HUB_USER_AGENT_ORIGIN;

            if (origin != null)
            {
                ua += "; origin/" + origin;
            }

            return _deduplicate_user_agent(ua);
        }

        /// <summary>
        /// Deduplicate redundant information in the generated user-agent.
        /// </summary>
        /// <param name="user_agent"></param>
        /// <returns></returns>
        private static string _deduplicate_user_agent(string user_agent)
        {
            // Split around ";" > Strip whitespaces > Store as dict keys (ensure unicity) > format back as string
            // Order is implicitly preserved by dictionary structure (see https://stackoverflow.com/a/53657523).
            return string.Join("; ", user_agent.Split(';').Select(item => item.Trim()));
        }

        public static async Task<string> get_token_to_send(dynamic token)
        {
            if (token is string str)
            {
                return str;
            }

            if (token == false)
            {
                return null;
            }

            // Case token is explicitly required
            if (token == true)
            {
                var cached_token = await auth.get_token();

                if (cached_token == null)
                {
                    throw new LocalTokenNotFoundError("Token is required (`token=True`), but no token found. You need to provide a token or be logged in to Hugging Face with `hf auth login` or `huggingface_hub.login`. See https://huggingface.co/settings/tokens.");
                }

                return cached_token;
            }

            // Case implicit use of the token is forbidden by env variable. Checked before resolving the
            // cached token: `get_token()` may refresh an OAuth token (network call + file writes), which
            // must not happen when the resolved token wouldn't be used anyway.
            if (constants.HF_HUB_DISABLE_IMPLICIT_TOKEN)
            {
                return null;
            }

            // Otherwise: we use the cached token as the user has not explicitly forbidden it
            return await auth.get_token();
        }
    }
}
