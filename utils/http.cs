using HuggingFaceHubSharp.errors;
using HuggingFaceHubSharp.Model;
using Newtonsoft.Json;
using PyException.common;
using PyException.httpx;
using SharpUtility.Extension;
using SharpUtility.Helper;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;

namespace HuggingFaceHubSharp.utils
{
    public class http
    {
        private const int _MAX_REDIRECTS = 2;
        private static readonly int[] _DEFAULT_RETRY_ON_STATUS_CODES = [408, 429, 500, 502, 503, 504];

        // Regex patterns for parsing rate limit headers
        // e.g.: "api";r=0;t=55 --> resource_type="api", r=0, t=55
        private const string _RATELIMIT_REGEX = @"""(?P<resource_type>\w+)""\s*;\s*r\s*=\s*(?P<r>\d+)\s*;\s*t\s*=\s*(?P<t>\d+)";

        // e.g.: "fixed window";"api";q=500;w=300 --> q=500, w=300
        private const string _RATELIMIT_POLICY_REGEX = @"q\s*=\s*(?P<q>\d+).*?w\s*=\s*(?P<w>\d+)";
        private static HashSet<string> _WARNED_TOPICS = new HashSet<string>();

        // Regex to extract repo_type and repo_id from API URLs.
        // Captures: group(1) = repo_type plural (models/datasets/spaces), group(2) = first path segment, group(3) = optional second segment.
        private const string _REPO_ID_FROM_URL_REGEX = @"^https?://[^/]+/api/(models|datasets|spaces)/([^/]+)(?:/([^/]+))?";
        // Regex to extract bucket_id (namespace/name) from bucket API URLs.
        private const string _BUCKET_ID_FROM_URL_REGEX = @"^https?://[^/]+/api/buckets/([^/]+/[^/]+)";
        // Regex to extract the job_id from a (scheduled) job API URL.
        // Matches /api/jobs/{namespace}/{job_id}[/...] and /api/scheduled-jobs/{namespace}/{job_id}[/...].
        private const string _JOB_ID_FROM_URL_REGEX = @"^https?://[^/]+/api/(?:scheduled-jobs|jobs)/[^/]+/([^/?]+)";
        // Sub-paths that follow a repo_id in API URLs (not part of the repo name).
        private static readonly string[] _REPO_URL_SUBPATHS = { "resolve", "tree", "blob", "raw", "refs", "commit", "discussions", "settings", "revision" };


        // When raising an error, we include the request id in the error message for easier debugging.
        // Request ID is sourced from headers in order of precedence: "X-Request-Id", "X-Amzn-Trace-Id", "X-Amz-Cf-Id".
        public const string X_REQUEST_ID = "x-request-id";
        public const string X_AMZN_TRACE_ID = "X-Amzn-Trace-Id";
        public const string X_AMZ_CF_ID = "x-amz-cf-id";
        public const string BUCKET_API_REGEX = @"^https?://[^/]+/api/buckets/";
        public const string REPO_API_REGEX = @"^https://[^/]+/api/(models|datasets|spaces)/(.+)/(.+)/resolve/(.+)";
        // Regex to parse HTTP Range header
        public const string RANGE_REGEX = @"^\s*bytes\s*=\s*(\d*)\s*-\s*(\d*)\s*$";

        /// <summary>
        /// Perform an HTTP request with backoff, following redirects that stay on the Hub.
        /// </summary>
        /// <param name="method"></param>
        /// <param name="url"></param>
        /// <param name="retry_on_errors"></param>
        /// <returns></returns>
        public static async Task<HttpResponseMessage> httpx_follow_hub_redirects_with_backoff(string method, string url, bool retry_on_errors = false, Dictionary<string, string> headers = null, float? timeout = null)
        {
            // if `retry_on_errors=False`, disable all retries for fast fallback to cache
            int[] retry_on_status_codes = [];
            Exception[] retry_on_exceptions = [];

            foreach (var _ in RangeHelper.GetRange(_MAX_REDIRECTS))
            {
                var response = await http_backoff(
                                method: method,
                                url: url,
                                timeout: timeout,
                                headers: headers,
                                follow_redirects: false,
                                retry_on_status_codes: retry_on_status_codes,
                                retry_on_exceptions: retry_on_exceptions
                                );

                if (response != null)
                {
                    await hf_raise_for_status(response);

                    if (!has_redirect_location(response))
                    {
                        return response;
                    }

                    var target = new Uri(new Uri(url), response.Headers.get("Location", "")).OriginalString;

                    if (!_is_same_or_hub_host(url, target))
                    {
                        // Redirect to a CDN or storage host (signed URL): the file metadata is carried by this very response,
                        // and the authorization header must not be forwarded off the Hub => stop here.
                        return response;
                    }

                    url = target;
                }
            }

            throw new TooManyRedirects($"Exceeded {_MAX_REDIRECTS} redirects while resolving '{url}'.");
        }

        /// <summary>
        /// Returns True for 3xx responses with a properly formed URL redirection,
        /// </summary>
        /// <param name="response"></param>
        /// <returns></returns>
        public static bool has_redirect_location(HttpResponseMessage response)
        {
            if (response != null && is_redirect(response) && response.Headers.Contains("Location"))
            {
                return true;
            }

            return false;
        }

        public static bool is_redirect(HttpResponseMessage response)
        {
            return response != null && ArrayHelper.GetArray(301, 302, 303, 307, 308).Contains((int)response.StatusCode);
        }

        /// <summary>
        /// Wrapper around httpx to retry calls on an endpoint, with exponential backoff.
        /// </summary>
        /// <param name="method"></param>
        /// <param name="url"></param>
        /// <param name="timeout"></param>
        /// <param name="headers"></param>
        /// <param name="follow_redirects"></param>
        /// <param name="max_retries"></param>
        /// <param name="base_wait_time"></param>
        /// <param name="max_wait_time"></param>
        /// <param name="retry_on_status_codes"></param>
        /// <param name="retry_on_exceptions"></param>
        /// <param name="kwargs"></param>
        /// <returns></returns>
        public static async Task<HttpResponseMessage> http_stream_backoff(string method,
                                string url,
                                float? timeout,
                                Dictionary<string, string> headers = null,
                                bool follow_redirects = true,
                                int max_retries = 5,
                                float base_wait_time = 1,
                                float max_wait_time = 8,
                                int[] retry_on_status_codes = null,
                                Exception[] retry_on_exceptions = null,
                                Dictionary<string, dynamic> kwargs = null)
        {
            return await _http_backoff_base(
                    method: method,
                    url: url,
                    max_retries: max_retries,
                    base_wait_time: base_wait_time,
                    max_wait_time: max_wait_time,
                    retry_on_exceptions: retry_on_exceptions,
                    retry_on_status_codes: retry_on_status_codes,
                    stream: true,
                    kwargs: kwargs
                );
        }

        public static async Task<HttpResponseMessage> http_backoff(string method,
                                string url,
                                float? timeout,
                                Dictionary<string, string> headers = null,
                                bool follow_redirects = true,
                                int max_retries = 5,
                                float base_wait_time = 1,
                                float max_wait_time = 8,
                                int[] retry_on_status_codes = null,
                                Exception[] retry_on_exceptions = null,
                                Dictionary<string, dynamic> kwargs = null)
        {
            kwargs ??= new Dictionary<string, dynamic>();

            retry_on_status_codes ??= _DEFAULT_RETRY_ON_STATUS_CODES;

            return await _http_backoff_base
                (
                    method: method,
                    url: url,
                    headers: headers,
                    timeout: timeout,
                    follow_redirects: follow_redirects,
                    max_retries: max_retries,
                    base_wait_time: base_wait_time,
                    max_wait_time: max_wait_time,
                    retry_on_exceptions: retry_on_exceptions,
                    retry_on_status_codes: retry_on_status_codes,
                    stream: false,
                    kwargs: kwargs
                );
        }

        private static async Task<HttpResponseMessage> _http_backoff_base(string method,
                                string url,
                                int max_retries = 2,
                                Dictionary<string, string> headers = null,
                                float? timeout = null,
                                bool follow_redirects = true,
                                float base_wait_time = 1,
                                float max_wait_time = 8,
                                Exception[] retry_on_exceptions = null,
                                int[] retry_on_status_codes = null,
                                bool stream = false,
                                Dictionary<string, dynamic> kwargs = null)
        {
            retry_on_exceptions ??= [];
            retry_on_status_codes ??= _DEFAULT_RETRY_ON_STATUS_CODES;
            kwargs ??= new Dictionary<string, dynamic>();

            var nb_tries = 0;
            var sleep_time = base_wait_time;
            int? ratelimit_reset = null;  // seconds to wait for rate limit reset if 429 response

            // If `data` is used and is a file object (or any IO), it will be consumed on the
            // first HTTP request. We need to save the initial position so that the full content
            // of the file is re-sent on http backoff. See warning tip in docstring.
            long? io_obj_initial_pos = null;

            if (kwargs.ContainsKey("data") /*&& kwargs["data"] is object*/)
            {
                io_obj_initial_pos = (kwargs["data"] as Stream).Position;
            }

            bool should_retry = false;

            while (nb_tries <= max_retries)
            {
                nb_tries += 1;
                ratelimit_reset = null;

                // Fetched on each attempt: a previous attempt may have closed the shared client (see `close_session` below),
                // and closed `httpx.Client` objects cannot be reused.
                using (var client = get_session())
                {
                    try
                    {
                        // If `data` is used and is a file object (or any IO), set back cursor to
                        // initial position.
                        if (io_obj_initial_pos is not null)
                        {
                            (kwargs["data"] as Stream).Seek(io_obj_initial_pos.Value, SeekOrigin.Begin); ////to do
                        }

                        // Perform request and handle response
                        /// Handle response and return True if should retry, False if should return/yield.
                        Func<HttpResponseMessage, Task<bool>> _should_retry = async (response) =>
                        {
                            if (!retry_on_status_codes.Contains((int)response.StatusCode))
                            {
                                return false;  // Success, don't retry
                            }

                            // Wrong status code returned (HTTP 503 for instance)
                            LogHelper.LogWarnning($"HTTP Error {response.StatusCode} thrown while requesting {method} {url}");

                            if (nb_tries > max_retries)
                            {
                                await hf_raise_for_status(response); // Will raise uncaught exception

                                // Return/yield response to avoid infinite loop in the corner case where the
                                // user ask for retry on a status code that doesn't raise_for_status.
                                return false;  // Don't retry, return/yield response
                            }

                            // Check 'ratelimit' and `Retry-After` headers.
                            var ratelimit_info = parse_ratelimit_headers(response.Headers);

                            if ((int)response.StatusCode == 429 && ratelimit_info is not null && ratelimit_info.remaining == 0)
                            {
                                ratelimit_reset = ratelimit_info.reset_in_seconds;
                            }
                            else
                            {
                                var retry_after = _parse_retry_after(response.Headers);

                                if (retry_after is not null)
                                {
                                    ratelimit_reset = retry_after;
                                }
                            }

                            return true;  // Should retry
                        };

                        HttpResponseMessage response = null;

                        if (method == "GET" || method == "HEAD")
                        {
                            response = await getResponse(url, method: method, parameters: kwargs.get<Dictionary<string, string>>("parameters", null), headers: headers, timeout: timeout, follow_redirects: follow_redirects, stream: stream);
                        }
                        else if (method == "PUT" || method == "POST")
                        {
                            ////to do
                        }

                        should_retry = await _should_retry(response);

                        if (!should_retry)
                        {
                            return response;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.LogWarnning($"'{ex.Message}' thrown while requesting {method} {url}");

                        if (ex is HttpRequestException)
                        {
                            close_session(client);
                        }

                        if (nb_tries > max_retries)
                        {
                            throw ex;
                        }
                    }

                    if (should_retry)
                    {
                        float actual_sleep = 0;

                        if (ratelimit_reset is not null)
                        {
                            actual_sleep = ratelimit_reset.Value + 1;  // +1s to avoid rounding issues
                            LogHelper.LogWarnning($"Rate limited. Waiting {actual_sleep}s before retry [Retry {nb_tries}/{max_retries}].");
                        }
                        else
                        {
                            actual_sleep = sleep_time;
                            LogHelper.LogWarnning($"Retrying in {actual_sleep}s [Retry {nb_tries}/{max_retries}].");
                        }

                        Thread.Sleep((int)(actual_sleep * 1000));

                        // Update sleep time for next retry
                        sleep_time = Math.Min(max_wait_time, sleep_time * 2);  // Exponential backoff
                    }
                }
            }

            return null;
        }

        public static HttpClient get_session(HttpClientHandler handler = null)
        {
            return handler != null ? new HttpClient(handler) : new HttpClient();
        }

        /// <summary>
        /// Close the global `httpx.Client` used by `huggingface_hub`.
        /// </summary>
        public static void close_session(HttpClient client)
        {
            // Then, close the clients
            if (client is not null)
            {
                try
                {
                    client.Dispose();
                }
                catch (Exception ex)
                {
                    LogHelper.LogWarnning($"Error closing client: {ex.Message}");
                }
            }
        }

        public static async Task<HttpResponseMessage> getResponse(string path, string method = "GET", Dictionary<string, string> parameters = null, Dictionary<string, string> headers = null, float? timeout = null, bool follow_redirects = true, bool? stream = null)
        {
            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = follow_redirects
            };

            HttpClient client = get_session(handler);

            if (timeout.HasValue && client.Timeout != TimeSpan.FromSeconds(100))
            {
                client.Timeout = TimeSpan.FromSeconds(timeout.Value);
            }

            var uri = parameters == null ? new UriBuilder(path) : new UriBuilder(path)
            {
                Query = await new FormUrlEncodedContent(parameters).ReadAsStringAsync()
            };

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            HttpResponseMessage r = null;

            if (method == "GET")
            {
                r = await client.GetAsync(uri.Uri, stream == true ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead);
            }
            else if (method == "HEAD")
            {
                var request = new HttpRequestMessage(HttpMethod.Head, uri.Uri);

                r = await client.SendAsync(request);

            }

            return r;
        }

        public static async Task<string> getContent(HttpResponseMessage response)
        {
            string content = await response.Content.ReadAsStringAsync();

            return content;
        }

        /// <summary>
        /// Internal version of `response.raise_for_status()` that will refine a potential HTTPError.
        /// Raised exception will be an instance of [`~errors.HfHubHTTPError`].
        /// </summary>
        /// <param name="response"></param>
        /// <param name="endpoint_name"></param>
        public static async Task hf_raise_for_status(HttpResponseMessage response, string endpoint_name = null)
        {
            if (response == null)
            {
                return;
            }

            try
            {
                _warn_on_warning_headers(response);
            }
            catch (Exception ex)
            {
                // Never raise on warning parsing
                LogHelper.LogError("Failed to parse warning headers");
            }

            try
            {
                raise_for_status(response);
            }
            catch (PyException.httpx.HTTPStatusError e)
            {
                if ((int)Math.Floor((int)response.StatusCode / 100.0) == 3)
                {
                    return;  // Do not raise on redirects to stay consistent with `requests`
                }

                var error_code = response.Headers.get("X-Error-Code");
                var error_message = response.Headers.get("X-Error-Message");

                // Parse repo info from request URL (used to enrich errors below)
                var request_url = response.RequestMessage is not null ? response.RequestMessage.RequestUri.AbsoluteUri : null;

                var (repo_type, repo_id) = request_url is not null ? _parse_repo_info_from_url(request_url) : (null, null);

                string message = null;

                if (error_code == "RevisionNotFound")
                {
                    message = $"{response.StatusCode} Client Error." + "\n\n" + $"Revision Not Found for url: {request_url}.";

                    throw await _format(typeof(RevisionNotFoundError), message, response, repo_type: repo_type, repo_id: repo_id);
                }
                else if (error_code == "EntryNotFound")
                {
                    message = $"{response.StatusCode} Client Error." + "\n\n" + $"Entry Not Found for url: {request_url}.";

                    throw await _format(typeof(RemoteEntryNotFoundError), message, response, repo_type: repo_type, repo_id: repo_id);
                }
                else if (error_code == "GatedRepo")
                {
                    message = $"{response.StatusCode} Client Error." + "\n\n" + $"Cannot access gated repo for url {request_url}.";

                    throw await _format(typeof(GatedRepoError), message, response, repo_type: repo_type, repo_id: repo_id);
                }
                else if (error_message == "Access to this resource is disabled.")
                {
                    message = $"{response.StatusCode} Client Error."
                    + "\n\n"
                    + $"Cannot access repository for url {request_url}."
                    + "\n"
                    + "Access to this resource is disabled.";

                    throw await _format(typeof(DisabledRepoError), message, response);
                }
                else if (error_code == "RepoNotFound"
                        && request_url is not null
                        && Regex.IsMatch(request_url, BUCKET_API_REGEX))
                {
                    message = $"{response.StatusCode} Client Error."
                            + "\n\n"
                            + $"Bucket Not Found for url: {request_url}."
                            + "\nPlease make sure you specified the correct bucket id (namespace/name)."
                            + "\nIf the bucket is private, make sure you are authenticated and your token has the required permissions.";

                    throw await _format(typeof(BucketNotFoundError), message, response, bucket_id: _parse_bucket_id_from_url(request_url));
                }
                else if ((int)response.StatusCode == 404
                        && request_url is not null
                        && (_parse_job_id_from_url(request_url)) is not null)
                {
                    var job_id = _parse_job_id_from_url(request_url);

                    message = $"{response.StatusCode} Client Error."
                            + "\n\n"
                            + $"Job Not Found for url: {request_url}."
                            + "\nPlease make sure you specified the correct job ID and namespace.";

                    throw await _format(typeof(JobNotFoundError), message, response, job_id: job_id);
                }
                else if (error_code == "RepoNotFound"
                    || ((int)response.StatusCode == 401
                    && error_message != "Invalid credentials in Authorization header"
                    && request_url is not null
                    && Regex.IsMatch(request_url, REPO_API_REGEX)))
                {
                    // 401 is misleading as it is returned for:
                    //    - private and gated repos if user is not authenticated
                    //    - missing repos
                    // => for now, we process them as `RepoNotFound` anyway.
                    // See https://gist.github.com/Wauplin/46c27ad266b15998ce56a6603796f0b9

                    message = $"{response.StatusCode} Client Error."
                            + "\n\n"
                            + $"Repository Not Found for url: {request_url}."
                            + "\nPlease make sure you specified the correct `repo_id` and"
                            + " `repo_type`.\nIf you are trying to access a private or gated repo,"
                            + " make sure you are authenticated and your token has the required permissions."
                            + "\nFor more details, see https://huggingface.co/docs/huggingface_hub/authentication";

                    throw await _format(typeof(RepositoryNotFoundError), message, response, repo_type: repo_type, repo_id: repo_id);
                }
                else if ((int)response.StatusCode == 400)
                {
                    message = endpoint_name is not null ? $"\n\nBad request for {endpoint_name} endpoint:" : "\n\nBad request:";

                    throw await _format(typeof(BadRequestError), message, response);
                }
                else if ((int)response.StatusCode == 403)
                {
                    message = $"\n\n{response.StatusCode} Forbidden: {error_message}."
                            + $"\nCannot access content at: {request_url}."
                            + "\nMake sure your token has the correct permissions.";

                    throw await _format(typeof(HfHubHTTPError), message, response);
                }
                else if ((int)response.StatusCode == 429)
                {
                    var ratelimit_info = parse_ratelimit_headers(response.Headers);

                    // Headers are set on every response: only use them if they are the reason for the 429.
                    if (ratelimit_info is not null && ratelimit_info.remaining == 0)
                    {
                        message = $"\n\n429 Too Many Requests: you have reached your '{ratelimit_info.resource_type}' rate limit.";
                        message += $"\nRetry after {ratelimit_info.reset_in_seconds} seconds";

                        if (ratelimit_info.limit is not null && ratelimit_info.window_seconds is not null)
                        {
                            message += $" ({ratelimit_info.remaining}/{ratelimit_info.limit} requests remaining" +
                                $" in current {ratelimit_info.window_seconds}s window).";
                        }
                        else
                        {
                            message += ".";
                        }

                        message += $"\nUrl: {request_url}.";
                    }
                    else
                    {
                        message = $"\n\n429 Too Many Requests for url: {request_url}.";
                    }

                    throw await _format(typeof(HfHubHTTPError), message, response);
                }
                else if ((int)response.StatusCode == 416)
                {
                    var range_header = response.RequestMessage.Headers.get("Range");

                    message = $"{e}. Requested range: {range_header}. Content-Range: {response.Headers.get("Content-Range")}.";

                    throw await _format(typeof(HfHubHTTPError), message, response);
                }

                // Convert `HTTPError` into a `HfHubHTTPError` to display request information
                // as well (request id and/or server error message)
                throw await _format(typeof(HfHubHTTPError), e.Message, response);
            }
        }

        /// <summary>
        /// Extract (repo_type, repo_id) from an API URL.
        /// </summary>
        /// <param name="url"></param>
        /// <returns>Returns canonical repo_type values: "model", "dataset", "space" (or None).</returns>
        private static (string, string) _parse_repo_info_from_url(string url)
        {
            var match = Regex.Match(url.ToString(), _REPO_ID_FROM_URL_REGEX);

            if (!match.Success)
            {
                return (null, null);
            }

            var repo_type = constants.REPO_TYPES_MAPPING.GetValueOrDefault(match.Groups[1].Value);
            var (first, second) = (match.Groups[2].Value, match.Groups[3].Value);

            string repo_id = null;

            if (!string.IsNullOrEmpty(second) && !_REPO_URL_SUBPATHS.Contains(second))
            {
                repo_id = $"{first}/{second}";
            }
            else
            {
                repo_id = first;
            }

            return (repo_type, repo_id);
        }

        /// <summary>
        ///  Raise the `HTTPStatusError` if one occurred.
        /// </summary>
        /// <param name="response"></param>
        public static HttpResponseMessage raise_for_status(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return response;
            }

            var status_class = (int)Math.Floor((int)response.StatusCode / 100.0);

            var error_types = new Dictionary<int, string>
            {
                {1, "Informational response" },
                {3, "Redirect response" },
                {4, "Client error" },
                {5, "Server error" }
            };

            var error_type = error_types.GetValueOrDefault(status_class, "Invalid status code");

            string message = null;

            if (has_redirect_location(response))
            {
                message = $"{error_type} '{response.StatusCode} {response.ReasonPhrase}' for url '{response.RequestMessage.RequestUri}'\n" +
                     $"Redirect location: '{response.Headers.get("Location")}'\n";
            }
            else
            {
                message = $"{error_type} '{response.StatusCode} {response.ReasonPhrase}' for url '{response.RequestMessage.RequestUri}'\n" +
                    "For more information check: https://developer.mozilla.org/en-US/docs/Web/HTTP/Status/{0.status_code}";
            }

            throw new HTTPStatusError(message, response: response);
        }

        private static void _warn_on_warning_headers(HttpResponseMessage response)
        {
            if (!response.Content.Headers.TryGetValues("X-HF-Warning", out _))
            {
                return;
            }

            var server_warnings = response.Content.Headers.GetValues("X-HF-Warning");

            foreach (var server_warning in server_warnings)
            {
                var items = server_warning.Split(';');

                var topic = items.Length > 0 ? items[0].Trim() : "";
                var message = items.LastOrDefault().Trim();

                if (topic.Length > 0 && message.Length > 0 && !_WARNED_TOPICS.Contains(topic))
                {
                    _WARNED_TOPICS.Add(topic);

                    LogHelper.LogWarnning(message);
                }
            }
        }

        /// <summary>
        /// Parse rate limit information from HTTP response headers.
        /// </summary>
        /// <param name="headers"></param>
        /// <returns></returns>
        public static RateLimitInfo parse_ratelimit_headers(HttpResponseHeaders headers)
        {
            string ratelimit = null;
            string policy = null;

            foreach (var item in headers)
            {
                var lower_key = item.Key.ToLower();

                if (lower_key == "ratelimit")
                {
                    ratelimit = item.Value.FirstOrDefault();
                }
                else if (lower_key == "ratelimit-policy")
                {
                    policy = item.Value.FirstOrDefault();
                }
            }

            if (string.IsNullOrEmpty(ratelimit))
            {
                return null;
            }

            var match = Regex.Match(ratelimit, _RATELIMIT_REGEX);

            if (!match.Success)
            {
                return null;
            }

            var resource_type = match.Groups["resource_type"].Value;
            var remaining = int.Parse(match.Groups["r"].Value);
            var reset_in_seconds = int.Parse(match.Groups["t"].Value);

            int? limit = null;
            int? window_seconds = null;

            if (!string.IsNullOrEmpty(policy))
            {
                var policy_match = Regex.Match(policy, _RATELIMIT_POLICY_REGEX);

                if (policy_match.Success)
                {
                    limit = int.Parse(policy_match.Groups["q"].Value);
                    window_seconds = int.Parse(policy_match.Groups["w"].Value);
                }
            }

            return new RateLimitInfo()
            {
                resource_type = resource_type,
                remaining = remaining,
                reset_in_seconds = reset_in_seconds,
                limit = limit,
                window_seconds = window_seconds,
            };
        }

        /// <summary>
        /// Parse the standard `Retry-After` HTTP header into a number of seconds to wait.
        /// </summary>
        /// <param name="headers"></param>
        /// <returns></returns>
        private static int? _parse_retry_after(HttpResponseHeaders headers)
        {
            string value = null;

            foreach (var item in headers)
            {
                var key = item.Key;

                if (key.ToLower() == "retry-after")
                {
                    value = item.Value.FirstOrDefault();
                    break;
                }
            }

            if (value is null)
            {
                return null;
            }

            value = value.Trim();

            if (value.Length == 0)
            {
                return null;
            }

            if (float.TryParse(value, out var v))
            {
                return (int)v;
            }

            return null; //  e.g. "Retry-After: Wed, 21 Oct 2015 07:28:00 GMT" - not supported
        }

        /// <summary>
        /// Returns the parsed header links of the response, if any
        /// </summary>
        /// <param name="response"></param>
        /// <returns></returns>
        public static Dictionary<string, Dictionary<string, string>> getLinks(HttpResponseMessage response)
        {
            Dictionary<string, Dictionary<string, string>> result = new Dictionary<string, Dictionary<string, string>>();

            if (response.Headers.TryGetValues("link", out _))
            {
                var header = response.Headers.GetValues("link").FirstOrDefault();

                foreach (var link in _parse_header_links(header))
                {
                    result.Add(link.GetValueOrDefault("rel") ?? link.GetValueOrDefault("url"), link);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns a list of parsed link headers, for more info see:
        /// https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Link
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        private static List<Dictionary<string, string>> _parse_header_links(string value)
        {
            List<Dictionary<string, string>> links = new List<Dictionary<string, string>>();

            var replace_chars = " '\"".ToCharArray();

            value = value.Trim(replace_chars);

            if (string.IsNullOrEmpty(value))
            {
                return links;
            }

            foreach (var val in Regex.Split(value, @", *<"))
            {
                string url = null;
                string parameters = null;

                try
                {
                    var items = val.Split(";", 2, StringSplitOptions.None);

                    url = items[0];
                    parameters = items[1];
                }
                catch (Exception ex)
                {
                    url = val;
                    parameters = "";
                }

                var link = new Dictionary<string, string> { { "url", url.Trim("<> '\"".ToCharArray()) } };

                foreach (var p in parameters.Split(';'))
                {
                    string key = null;
                    value = null;

                    try
                    {
                        var items = p.Split('=');

                        key = items[0];
                        value = items[1];
                    }
                    catch (Exception ex)
                    {
                        break;
                    }

                    link[key.Trim(replace_chars)] = value.Trim(replace_chars);
                }

                links.Add(link);
            }

            return links;
        }

        /// <summary>
        /// Whether `target` is served by the same host as `url`, or by a known Hub host.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        internal static bool _is_same_or_hub_host(string url, string target)
        {
            if (url == target)
            {
                return true;
            }

            var host = new Uri(url ?? "").Host.ToLower();
            var target_host = new Uri(target ?? "").Host.ToLower();

            return host == target_host || constants.HF_URL_HOSTS.Contains(target_host);
        }

        private static async Task<HfHubHTTPError> _format(Type error_type, string custom_message, HttpResponseMessage response,
                     string repo_type = null, string repo_id = null, string bucket_id = null, string job_id = null)
        {
            var server_errors = new List<string>();

            // Retrieve server error from header
            var from_headers = response.Headers.get("X-Error-Message");

            if (from_headers is not null)
            {
                server_errors.Add(from_headers);
            }

            string text = null;

            // Retrieve server error from body
            try
            {
                Dictionary<string, dynamic> data = null;

                // Case errors are returned in a JSON format
                try
                {
                    text = await response.Content.ReadAsStringAsync();

                    data = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(text); // In case of streaming response, we need to read the response first
                }
                catch (Exception ex)
                {
                    // In case of async streaming response, we can't read the stream here.
                    // In practice if user is using the default async client from `get_async_client`, the stream will have
                    // already been read in the async event hook `async_hf_response_event_hook`.
                    //
                    // Here, we are skipping reading the response to avoid RuntimeError but it happens only if async + stream + used httpx.AsyncClient directly.
                    data = new Dictionary<string, dynamic>();
                }

                var error = data.GetValueOrDefault("error");
                var error_description = data.GetValueOrDefault("error_description");

                if (error is not null)
                {
                    if (ObjectHelper.IsList(error))
                    {
                        // Case {'error': ['my error 1', 'my error 2']}
                        server_errors.AddRange(error as List<string>);
                    }
                    else if (error_description is not null)
                    {
                        // OAuth-style case {'error': 'invalid_grant', 'error_description': 'my description'}
                        server_errors.Add($"{error}: {error_description}");
                    }
                    else
                    {
                        // Case {'error': 'my error'}
                        server_errors.Add(error);
                    }
                }
                else if (error_description is not null)
                {
                    // Case {'error_description': 'my description'} (no 'error' field)
                    server_errors.Add(error_description);
                }

                var errors = data.GetValueOrDefault("errors");

                if (errors is not null)
                {
                    // Case {'errors': [{'message': 'my error 1'}, {'message': 'my error 2'}]}
                    foreach (var e in errors)
                    {
                        if (e.Contains("message"))
                        {
                            server_errors.Add(e["message"]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // If content is not JSON and not HTML, append the tex
                var content_type = response.Headers.get("Content-Type", "");

                if (!string.IsNullOrEmpty(text) && !content_type.ToLower().Contains("html"))
                {
                    server_errors.Add(text);
                }
            }

            // Strip all server messages
            server_errors = server_errors.Where(line => !string.IsNullOrEmpty(line.Trim())).Select(line => line.Trim()).ToList();

            // Deduplicate server messages (keep order)
            // taken from https://stackoverflow.com/a/17016257
            server_errors = server_errors.ToHashSet().ToList();

            // Format server error
            var server_message = string.Join("\n", server_errors);

            // Add server error to custom message
            var final_error_message = custom_message;

            if (!string.IsNullOrEmpty(server_message) && !custom_message.ToLower().Contains(server_message.ToLower()))
            {
                if (custom_message.Contains("\n\n"))
                {
                    final_error_message += "\n" + server_message;
                }
                else
                {
                    final_error_message += "\n\n" + server_message;
                }
            }

            // Prepare Request ID message
            var request_id = "";
            var request_id_message = "";

            var labels = new List<(string, string)>() { (X_REQUEST_ID, "Request ID"), (X_AMZN_TRACE_ID, "Amzn Trace ID"), (X_AMZ_CF_ID, "Amz CF ID") };

            foreach (var (header, label) in labels)
            {
                var value = response.Headers.get(header);

                if (!string.IsNullOrEmpty(value))
                {
                    request_id = value;
                    request_id_message = $" ({label}: {value})";

                    break;
                }
            }

            // Add Request ID
            if (!string.IsNullOrEmpty(request_id) && !final_error_message.ToLower().Contains(request_id.ToLower()))
            {
                if (final_error_message.Contains("\n"))
                {
                    var newline_index = final_error_message.IndexOf("\n");

                    final_error_message = final_error_message.Substring(0, newline_index) + request_id_message + final_error_message.Substring(newline_index);
                }
                else
                {
                    final_error_message += request_id_message;
                }
            }

            object[] pamameters = [final_error_message.Trim(), response, server_message];

            // Return
            var err = Activator.CreateInstance(error_type, pamameters) as HfHubHTTPError;

            err.repo_type = repo_type;
            err.repo_id = repo_id;
            err.bucket_id = bucket_id;
            err.job_id = job_id;

            return err;
        }

        /// <summary>
        /// Extract bucket_id (namespace/name) from a bucket API URL.
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private static string _parse_bucket_id_from_url(string url)
        {
            var match = Regex.Match(url, _BUCKET_ID_FROM_URL_REGEX);

            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Extract the job_id from a (scheduled) job API URL, if present.
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private static string _parse_job_id_from_url(string url)
        {
            var match = Regex.Match(url, _JOB_ID_FROM_URL_REGEX);

            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Adjust HTTP Range header to account for resume position.
        /// </summary>
        /// <returns></returns>
        internal static string _adjust_range_header(string original_range, long resume_size)
        {
            if (string.IsNullOrEmpty(original_range))
            {
                return $"bytes={resume_size}-";
            }

            if (original_range.Contains(","))
            {
                throw new ValueError($"Multiple ranges detected - {original_range}, not supported yet.");
            }

            var match = Regex.Match(original_range, RANGE_REGEX, RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                throw new RuntimeError($"Invalid range format - {original_range}.");
            }

            var (start, end) = (match.Groups[0].Value, match.Groups[1].Value);

            if (string.IsNullOrEmpty(start))
            {
                if (string.IsNullOrEmpty(end))
                {
                    throw new RuntimeError($"Invalid range format - {original_range}.");
                }

                var new_suffix = long.Parse(end) - resume_size;
                var new_range = $"bytes=-{new_suffix}";

                if (new_suffix <= 0)
                {
                    throw new RuntimeError($"Empty new range - {new_range}.");
                }

                return new_range;
            }

            var new_start = long.Parse(start) + resume_size;

            if (!string.IsNullOrEmpty(end))
            {
                var new_range = $"bytes={new_start}-{end}";

                if (new_start > long.Parse(end))
                {
                    throw new RuntimeError($"Empty new range - {new_range}.");
                }

                return new_range;
            }

            return $"bytes={new_start}-";
        }
    }
}
