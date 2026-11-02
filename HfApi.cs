using HuggingFaceHubSharp.Model;
using HuggingFaceHubSharp.utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PyException.common;
using SharpUtility.Extension;
using System.Web;

namespace HuggingFaceHubSharp
{
    /// <summary>
    /// Client to interact with the Hugging Face Hub via HTTP.
    /// 
    /// The client is initialized with some high-level settings used in all requests
    /// made to the Hub(HF endpoint, authentication, user agents...). Using the `HfApi`
    /// client is preferred but not mandatory as all of its public methods are exposed
    /// directly at the root of `huggingface_hub`.
    /// </summary>
    public class HfApi
    {
        private Dictionary<string, Dictionary<string, dynamic>> _whoami_cache;

        public string endpoint { get; set; }
        public dynamic token { get; set; }
        public string library_name { get; set; }
        public string library_version { get; set; }
        public dynamic user_agent { get; set; }
        public Dictionary<string, string> headers { get; set; }


        public HfApi(string endpoint = null, dynamic token = null, string library_name = null, string library_version = null, dynamic user_agent = null, Dictionary<string, string> headers = null)
        {
            this.endpoint = endpoint ?? constants.ENDPOINT;
            this.token = token;
            this.library_name = library_name;
            this.library_version = library_version;
            this.user_agent = user_agent;
            this.headers = headers;

            // /whoami-v2 is the only endpoint for which we may want to cache results
            this._whoami_cache = new Dictionary<string, Dictionary<string, dynamic>>();
        }

        /// <summary>
        /// List a repo tree's files and folders and get information about them.
        /// </summary>
        /// <param name="repo_id"></param>
        /// <param name="path_in_repo"></param>
        /// <param name="recursive"></param>
        /// <param name="expand"></param>
        /// <param name="revision"></param>
        /// <param name="repo_type"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<List<RepoInfo>> list_repo_tree(string repo_id, string path_in_repo = null, bool recursive = false, bool expand = false, string revision = null, string repo_type = null, dynamic token = null)
        {
            repo_type = repo_type ?? constants.REPO_TYPE_MODEL;
            revision = revision != null ? HttpUtility.UrlEncode(revision) : constants.DEFAULT_REVISION;
            this.headers = await this._build_hf_headers(token: token);

            var encoded_path_in_repo = path_in_repo != null ? "/" + HttpUtility.UrlEncode(path_in_repo) : "";
            var tree_url = $"{this.endpoint}/api/{repo_type}s/{repo_id}/tree/{revision}{encoded_path_in_repo}";

            List<RepoInfo> results = new List<RepoInfo>();

            foreach (var path_info in await pagination.paginate(path: tree_url, headers: headers, parameters: new Dictionary<string, string> { { "recursive", recursive ? "1" : "0" }, { "expand", expand ? "1" : "0" } }))
            {
                results.Add(path_info.GetValueOrDefault("type") == "file" ? new RepoFile(path_info) : new RepoFolder(path_info));
            }

            return results;
        }

        /// <summary>
        /// Alias for [`build_hf_headers`] that uses the token from [`HfApi`] client
        /// when `token` is not provided.
        /// </summary>
        /// <param name="token"></param>
        /// <param name="library_name"></param>
        /// <param name="library_version"></param>
        /// <param name="user_agent"></param>
        /// <returns></returns>
        public async Task<Dictionary<string, string>> _build_hf_headers(dynamic token = null, string library_name = null, string library_version = null, dynamic user_agent = null)
        {
            if (token == null)
            {
                //Cannot do `token = token or this.token` as token can be `False`.
                token = this.token;
            }

            return await utils.headers.build_hf_headers(
                token: token,
                library_name: library_name ?? this.library_name,
                library_version: library_version ?? this.library_version,
                user_agent: user_agent ?? this.user_agent,
                headers: this.headers
            );
        }

        /// <summary>
        /// Download repo files.
        /// 
        /// Download a whole snapshot of a repo's files at the specified revision. This is useful when you want all files from
        /// a repo, because you don't know which ones you will need a priori. All files are nested inside a folder in order
        /// to keep their actual filename relative to that folder.You can also filter which files to download using
        /// `allow_patterns` and `ignore_patterns`.
        /// </summary>
        /// <param name="repo_id"></param>
        /// <param name="repo_type"></param>
        /// <param name="revision"></param>
        /// <param name="cache_dir"></param>
        /// <param name="local_dir"></param>
        /// <param name="etag_timeout"></param>
        /// <param name="force_download"></param>
        /// <param name="token"></param>
        /// <param name="local_files"></param>
        /// <param name="allow_patterns"></param>
        /// <param name="ignore_patterns"></param>
        /// <param name="max_workers"></param>        
        /// <param name="dry_run"></param>
        /// <returns></returns>
        public async Task<dynamic> snapshot_download(string repo_id,
                                        string repo_type = null,
                                        string revision = null,
                                        string cache_dir = null,
                                        string local_dir = null,
                                        string library_name = null,
                                        string library_version = null,
                                        dynamic user_agent = null,
                                        float etag_timeout = constants.DEFAULT_ETAG_TIMEOUT,
                                        bool force_download = false,
                                        dynamic token = null,
                                        bool local_files_only = false,
                                        dynamic allow_patterns = null,
                                        dynamic ignore_patterns = null,
                                        int max_workers = 8,
                                        Dictionary<string, string> headers = null,
                                        string endpoint = null,
                                        bool dry_run = false)
        {
            if (token == null)
            {
                // Cannot do `token = token or this.token` as token can be `False`.
                token = this.token;
            }

            return await HuggingFaceHubSharp.snapshot_download.download(
                    repo_id: repo_id,
                    repo_type: repo_type,
                    revision: revision,
                    endpoint: this.endpoint,
                    cache_dir: cache_dir,
                    local_dir: local_dir,
                    library_name: this.library_name,
                    library_version: this.library_version,
                    user_agent: this.user_agent,
                    etag_timeout: etag_timeout,
                    force_download: force_download,
                    token: token,
                    local_files_only: local_files_only,
                    allow_patterns: allow_patterns,
                    ignore_patterns: ignore_patterns,
                    max_workers: max_workers,
                    headers: this.headers,
                    dry_run: dry_run
            );
        }

        public dynamic repo_info(string repo_id, string revision = null, string repo_type = null,
                                 float? timeout = null, float? files_metadata = null, string expand = null, dynamic token = null)
        {
            dynamic method = null;

            //switch (repo_type)
            //{
            //    case null:
            //    case "model":
            //        method = this.model_info;
            //        break;
            //    case "dataset":
            //        method = this.dataset_info; // type: ignore
            //        break;
            //    case "space":
            //        method = this.space_info;  //type: ignore
            //        break;
            //    case "kernel":
            //        // No expand/files_metadata for kernels
            //        return this.kernel_info(repo_id, revision : revision, token : token, timeout : timeout);
            //    defalut:
            //        throw new ValueError("Unsupported repo type.");
            //        break;
            //}

            return null;////to do
        }

        public async Task<ModelInfo> model_info(string repo_id, string revision = null, float? timeout = null,
                                    bool? securityStatus = false, bool? files_metadata = false,
                                    string expand = null, dynamic token = null)
        {
            if (expand != null && (securityStatus == true || files_metadata == true))
            {
                throw new ValueError("`expand` cannot be used if `securityStatus` or `files_metadata` are set.");
            }

            Dictionary<string, string> headers = await this._build_hf_headers(token: token);
            var path = revision == null ? $"{this.endpoint}/api/models/{repo_id}" : $"{this.endpoint}/api/models/{repo_id}/revision/{HttpUtility.UrlDecode(revision)}";

            var parameters = new Dictionary<string, string>();

            if (securityStatus == true)
                parameters["securityStatus"] = "1";
            if (files_metadata == true)
                parameters["blobs"] = "1";
            if (expand != null)
                parameters["expand"] = expand;

            string content = await http.getContent(await http.getResponse(path, parameters: parameters, headers: headers, timeout: timeout));

            var model = JsonConvert.DeserializeObject<ModelInfo>(content);

            return model;
        }

        /// <summary>
        /// Get the list of files in a given repo.
        /// </summary>
        /// <param name="repo_id"></param>
        /// <param name="revision"></param>
        /// <param name="repo_type"></param>
        /// <param name="token"></param>
        /// <returns>the list of files in a given repository.</returns>
        public async Task<List<string>> list_repo_files(string repo_id, string revision = null, string repo_type = null, dynamic token = null)
        {
            var tree = await this.list_repo_tree(repo_id: repo_id, recursive: true, revision: revision, repo_type: repo_type, token: token);

            List<string> list = new List<string>();

            foreach (var item in tree)
            {
                if (item is RepoFile f)
                {
                    list.Add(f.rfilename);
                }
            }

            return list;
        }

        /// <summary>
        /// Get the list of commits of a given revision for a repo on the Hub.
        /// 
        /// Commits are sorted by date (last commit first).
        /// </summary>
        /// <param name="repo_id"></param>
        /// <param name="repo_type"></param>
        /// <param name="token"></param>
        /// <param name="revision"></param>
        /// <param name="formatted"></param>
        /// <returns></returns>
        public async Task<List<GitCommitInfo>> list_repo_commits(string repo_id, string repo_type = null, dynamic token = null, string revision = null, bool formatted = false)
        {
            repo_type = repo_type ?? constants.REPO_TYPE_MODEL;
            revision = revision is not null ? HttpUtility.UrlEncode(revision) : constants.DEFAULT_REVISION;

            // Paginate over results and return the list of commits.
            List<Dictionary<string, dynamic>> items = await pagination.paginate($"{this.endpoint}/api/{repo_type}s/{repo_id}/commits/{revision}",
                    headers: await this._build_hf_headers(token: token),
                    parameters: formatted ? new Dictionary<string, string>() { { "expand[]", "formatted" } } : new Dictionary<string, string>()
            );

            List<GitCommitInfo> results = new List<GitCommitInfo>();

            foreach (Dictionary<string, dynamic> item in items)
            {
                results.Add(new GitCommitInfo()
                {
                    commit_id = item["id"],
                    authors = (item.get<JArray>("authors"))?.Select(item => (item["user"] as JValue).Value.ToString())?.ToList(),
                    created_at = Convert.ToDateTime((item["date"])),
                    title = item["title"],
                    message = item["message"],
                    formatted_title = item.get<Dictionary<string, dynamic>>("formatted", new Dictionary<string, dynamic>()).get<string>("title"),
                    formatted_message = item.get<Dictionary<string, dynamic>>("formatted", new Dictionary<string, dynamic>()).get<string>("message")
                });
            }

            return results;
        }

        /// <summary>
        /// Fetches Discussions and Pull Requests for the given repo.
        /// </summary>
        /// <param name="repo_id"></param>
        /// <param name="author"></param>
        /// <param name="discussion_type"></param>
        /// <param name="discussion_status"></param>
        /// <param name="repo_type"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<List<Discussion>> get_repo_discussions(string repo_id, string author = null, string discussion_type = null, string discussion_status = null, string repo_type = null, dynamic token = null)
        {
            if (!constants.REPO_TYPES.Contains(repo_type))
            {
                throw new ValueError($"Invalid repo type, must be one of {constants.REPO_TYPES.ToFridenlyString()}");
            }

            if (repo_type is null)
            {
                repo_type = constants.REPO_TYPE_MODEL;
            }

            if (discussion_type is not null && !constants.DISCUSSION_TYPES.Contains(discussion_type))
            {
                throw new ValueError($"Invalid discussion_type, must be one of {constants.DISCUSSION_TYPES.ToFridenlyString()}");
            }

            headers = await this._build_hf_headers(token: token);
            var path = $"{this.endpoint}/api/{repo_type}s/{repo_id}/discussions";

            Dictionary<string, string> parameters = new Dictionary<string, string>();

            if (discussion_type is not null)
            {
                parameters["type"] = discussion_type;
            }

            if (discussion_status is not null)
            {
                parameters["status"] = discussion_status;
            }

            if (author is not null)
            {
                parameters["author"] = author;
            }

            Func<int, Task<(List<Dictionary<string, dynamic>>, bool)>> _fetch_discussion_page = async (page_index) =>
            {
                parameters["p"] = page_index.ToString();

                HttpResponseMessage resp = await http.getResponse(path, headers: headers, parameters: parameters);
                string content = await http.getContent(resp);

                var paginated_discussions = JsonConvert.DeserializeObject<Dictionary<string, dynamic>>(content);
                var total = paginated_discussions["count"];
                var start = paginated_discussions["start"];
                var discussions = paginated_discussions.get<JArray>("discussions").ToArray().Select(item => item.ToObject<Dictionary<string, dynamic>>()).ToList();
                var has_next = (start + discussions.Count) < total;

                return (discussions, has_next);
            };

            var has_next = true;
            var page_index = 0;

            List<Discussion> results = new List<Discussion>();

            while (has_next)
            {
                var result = await _fetch_discussion_page(page_index);

                var discussions = result.Item1;
                has_next = result.Item2;

                foreach (var discussion in discussions)
                {
                    var repo = discussion.get<JObject>("repo")?.ToObject<Dictionary<string, dynamic>>();

                    results.Add(new Discussion()
                    {
                        title = discussion.get<string>("title"),
                        num = (int)discussion.get<float>("num"),
                        author = discussion.get<JObject>("author")?.ToObject<Dictionary<string, dynamic>>()?.get("name", "deleted"),
                        created_at = Convert.ToDateTime(discussion["createdAt"]),
                        status = discussion.get<string>("status"),
                        repo_id = repo?.get<string>("name"),
                        repo_type = repo?.get<string>("type"),
                        is_pull_request = discussion.get<bool>("isPullRequest"),
                        endpoint = this.endpoint
                    });
                }

                page_index = page_index + 1;
            }

            return results;
        }

        /// <summary>
        /// Checks if a file exists in a repository on the Hugging Face Hub.
        /// </summary>
        /// <param name="repo_id"></param>
        /// <param name="filename"></param>
        /// <param name="repo_type"></param>
        /// <param name="revision"></param>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<bool> file_exists(string repo_id, string filename, string repo_type = null, string revision = null, dynamic token = null)
        {
            var url = file_download.hf_hub_url(repo_id: repo_id, repo_type: repo_type, revision: revision, filename: filename, endpoint: this.endpoint);

            try
            {
                if (token is null)
                {
                    token = this.token;
                }

                var result = await file_download.get_hf_file_metadata(url, token: token);

                return result != null;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
    }
}
