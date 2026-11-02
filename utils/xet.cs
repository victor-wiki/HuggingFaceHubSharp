using HuggingFaceHubSharp.Model;
using PyException.common;
using SharpUtility.Extension;
using System.Text.RegularExpressions;

namespace HuggingFaceHubSharp.utils
{
    public class xet
    {
        public const string REGEX_XET_HASH = @"^[0-9a-fA-F]{64}$";

        /// <summary>
        /// Return whether `xet_hash` looks like a real Xet content hash (64 hex chars).
        /// </summary>
        /// <param name="xet_hash"></param>
        /// <returns></returns>
        public static bool is_valid_xet_hash(string xet_hash)
        {
            return Regex.IsMatch(xet_hash, REGEX_XET_HASH);
        }

        /// <summary>
        /// Build the URL used to fetch or refresh a Xet access token for a given repo.
        /// </summary>
        /// <param name="token_type"></param>
        /// <param name="repo_id"></param>
        /// <param name="repo_type"></param>
        /// <param name="revision"></param>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        public static string xet_connection_info_refresh_url(XetTokenType token_type,
                                    string repo_id,
                                    string repo_type,
                                    string revision = null,
                                    string endpoint = null)
        {
            endpoint ??= constants.ENDPOINT;

            var url = $"{endpoint}/api/{repo_type}s/{repo_id}/xet-{token_type.ToString().ToLower()}-token";

            if (repo_type != "bucket" || revision != null)
            {
                // On "bucket" repo type, the revision never needed => don't use it
                // Otherwise, use the revision.
                // Note: when creating a PR on a git-based repo, user needs write access but they don't know the revision in advance.
                // => pass "/null" in URL and server will return a token for PR refs.
                url += $"/{revision}";
            }

            return url;
        }

        public static XetFileData parse_xet_file_data_from_response(HttpResponseMessage response, string endpoint)
        {
            if (response == null)
            {
                return null;
            }

            string refresh_route = null;
            string file_hash = null;

            try
            {
                file_hash = response.Headers.get(constants.HUGGINGFACE_HEADER_X_XET_HASH);

                if (file_hash == null)
                {
                    return null;
                }

                var links = http.getLinks(response);

                if (links.ContainsKey(constants.HUGGINGFACE_HEADER_LINK_XET_AUTH_KEY))
                {
                    refresh_route = links[constants.HUGGINGFACE_HEADER_LINK_XET_AUTH_KEY]["url"];
                }
                else
                {
                    refresh_route = response.Headers.get(constants.HUGGINGFACE_HEADER_X_XET_REFRESH_ROUTE);
                }
            }
            catch (KeyError ex)
            {
                return null;
            }

            endpoint = endpoint != null ? endpoint : constants.ENDPOINT;

            if (refresh_route != null && refresh_route.StartsWith(constants.HUGGINGFACE_CO_URL_HOME))
            {
                refresh_route = refresh_route.Replace(constants.HUGGINGFACE_CO_URL_HOME.TrimEnd('/'), endpoint.TrimEnd('/'));
            }

            return new XetFileData()
            {
                file_hash = file_hash,
                refresh_route = refresh_route,
            };
        }
    }
}
