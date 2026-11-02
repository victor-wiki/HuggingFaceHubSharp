using HuggingFaceHubSharp.errors;
using HuggingFaceHubSharp.Model;
using HuggingFaceHubSharp.utils;
using PyException.common;
using SharpUtility.Extension;
using SharpUtility.Helper;
using System.Text.RegularExpressions;

namespace HuggingFaceHubSharp
{
    public class snapshot_download
    {
        public static async Task<dynamic> download(
                                    string repo_id,
                                    string repo_type = null,
                                    dynamic revision = null,
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
            cache_dir ??= constants.HF_HUB_CACHE;
            cache_dir = Environment.ExpandEnvironmentVariables(cache_dir);

            if (local_dir != null)
            {
                local_dir = Environment.ExpandEnvironmentVariables(local_dir);
            }

            revision ??= constants.DEFAULT_REVISION;
            repo_type ??= "model";

            if (!constants.REPO_TYPES_WITH_KERNEL.Contains(repo_type))
            {
                throw new ValueError($"Invalid repo type: {repo_type}. Accepted repo types are: {constants.REPO_TYPES_WITH_KERNEL.ToFridenlyString()}");
            }

            var storage_folder = Path.Combine(cache_dir, file_download.repo_folder_name(repo_id: repo_id, repo_type: repo_type));

            // Folder under which the per-commit tree listing (`trees/<commit_hash>.json`) is cached on disk.
            var tree_cache_folder = local_dir != null ? tree_cache.tree_cache_folder_for_local_dir(local_dir) : storage_folder;

            var api = new HfApi(library_name: library_name, library_version: library_version, user_agent: user_agent, endpoint: endpoint, headers: headers, token: token);

            // The revision is already a commit hash if:
            // - it's a `ResolvedRevision` (already resolved, see [`HfApi.resolve_revision`])
            // - it's a commit hash, which is immutable
            // => in both cases, there is nothing to resolve and the `repo_info` call can be skipped.
            string commit_hash = null;

            if (revision is ResolvedRevision re)
            {
                commit_hash = re.resolved;
            }
            else if (revision is string str)
            {
                if (Regex.IsMatch(str, file_download.REGEX_COMMIT_HASH))
                {
                    commit_hash = str;
                }
            }

            Exception api_call_error = null;
            string snapshot_folder = null;

            if (commit_hash == null && !local_files_only)
            {
                // try/except logic to handle different errors => taken from `hf_hub_download`
                try
                {
                    //if we have internet connection we want to list files to download
                    var repo_info = api.repo_info(repo_id: repo_id, repo_type: repo_type, revision: revision);

                    commit_hash = repo_info.sha;
                }
                catch (Exception ex)
                {
                    api_call_error = ex;
                }
            }

            // At this stage, if the commit hash is unknown it means either:
            // - internet connection is down
            // - internet connection is deactivated (local_files_only=True or HF_HUB_OFFLINE=True)
            // - repo is private/gated and invalid/missing token sent
            // - Hub is down
            // => let's look if we can find the appropriate folder in the cache:
            //    - if the specified revision is a commit hash, look inside "snapshots".
            //    - f the specified revision is a branch or tag, look inside "refs".
            // => if local_dir is not None, we will return the path to the local folder if it exists.
            if (commit_hash == null || local_files_only)
            {
                if (dry_run)
                {
                    throw new DryRunError($"Dry run cannot be performed as the repository cannot be accessed. Please check your internet connection or authentication token.");
                }

                //Try to get which commit hash corresponds to the specified revision
                if (commit_hash == null)
                {
                    var ref_path = Path.Combine(storage_folder, "refs", revision);

                    if (File.Exists(ref_path))
                    {
                        // retrieve commit_hash from refs file
                        commit_hash = File.ReadAllText(ref_path);
                    }
                }

                // Try to locate snapshot folder for this commit hash
                if (commit_hash != null && local_dir == null)
                {
                    snapshot_folder = Path.Combine(storage_folder, "snapshots", commit_hash);

                    if (Directory.Exists(snapshot_folder))
                    {
                        // The folder exists, but may be partial (e.g. after an interrupted download): only return it
                        // if the cached tree listing confirms it is complete.
                        _raise_if_incomplete_snapshot(
                            tree_cache_folder: tree_cache_folder,
                            commit_hash: commit_hash,
                            base_dir: snapshot_folder,
                            allow_patterns: allow_patterns,
                            ignore_patterns: ignore_patterns,
                            repo_id: repo_id,
                            revision: revision,
                            api_call_error: api_call_error
                        );

                        return snapshot_folder;
                    }
                }

                // If local_dir is not None, return it if it exists and is complete
                if (local_dir != null)
                {
                    if (Directory.Exists(local_dir) && FolderHelper.HasSubFolder(local_dir))
                    {
                        if (commit_hash != null)
                        {
                            _raise_if_incomplete_snapshot(
                                tree_cache_folder: tree_cache_folder,
                                commit_hash: commit_hash,
                                base_dir: local_dir,
                                allow_patterns: allow_patterns,
                                ignore_patterns: ignore_patterns,
                                repo_id: repo_id,
                                revision: revision,
                                api_call_error: api_call_error
                            );
                        }

                        LogHelper.LogWarnning($"Returning existing local_dir `{local_dir}` as remote repo cannot be accessed in `snapshot_download` ({api_call_error}).");
                    }
                }

                // If we couldn't find the appropriate folder on disk, raise an error.
                if (local_files_only)
                {
                    throw new LocalEntryNotFoundError($"Cannot find an appropriate cached snapshot folder for the specified revision on the local disk and " +
                        "outgoing traffic has been disabled. To enable repo look-ups and downloads online, pass " +
                        "'local_files_only=False' as input.");
                }
                else
                {
                    throw api_call_error;
                }



            }

            // Retrieve /tree listing from cache or fetch it
            var tree_entries = tree_cache.read_tree_cache(tree_cache_folder, commit_hash);

            if (tree_entries == null)
            {
                var repoInfos = (await api.list_repo_tree(repo_id: repo_id, recursive: true, revision: commit_hash, repo_type: repo_type))
                                .Where(item => item is RepoFile).Select(item => item as RepoFile);

                foreach (var f in repoInfos)
                {
                    var entry = new TreeCacheEntry()
                    {
                        size = f.size,
                        blob_id = f.blob_id,
                        lfs_sha256 = f.lfs != null ? f.lfs.sha256 : null,
                        lfs_size = f.lfs != null ? f.lfs.size : null,
                        xet_hash = f.xet_hash
                    };

                    tree_entries.Add(f.path, entry);
                }

                if (!dry_run)
                {
                    tree_cache.write_tree_cache(tree_cache_folder, commit_hash, tree_entries);
                }
            }

            var filtered_repo_files = paths.filter_repo_objects(
                                    items: tree_entries.Keys,
                                    allow_patterns: allow_patterns,
                                    ignore_patterns: ignore_patterns);


            string tqdm_desc = $"Fetching {filtered_repo_files.Count()} files";

            if (dry_run)
            {
                tqdm_desc = "[dry-run] " + tqdm_desc;
            }

            snapshot_folder = Path.Combine(storage_folder, "snapshots", commit_hash);

            //if passed revision is not identical to commit_hash
            // then revision has to be a branch name or tag name.
            // In that case store a ref (except if ResolvedRevision, in which case it's already done).
            if (revision is not ResolvedRevision)
            {
                try
                {
                    // Skips the write if the ref is already up to date, and writes atomically otherwise so that
                    // concurrent readers never observe a truncated (empty) ref file.
                    file_download.cache_commit_hash_for_specific_revision(storage_folder, revision, commit_hash);
                }
                catch (Exception ex)
                {
                    LogHelper.LogWarnning($"Ignored error while writing commit hash to {Path.Combine(storage_folder, "refs", revision)}: {ex.Message}.");
                }
            }

            var results = new List<dynamic>();

            if (dry_run)
            {
                return results;
            }

            if (local_dir != null)
            {
                return local_dir;
            }

            return snapshot_folder; //// to do;
        }

        /// <summary>
        /// Raise [`IncompleteSnapshotError`] if the cached tree listing shows `base_dir` misses requested files.
        /// 
        /// If the tree listing is not cached we cannot tell, so we do nothing and the caller keeps returning the
        /// folder as-is. Otherwise every expected file(after pattern filtering) must exist under `base_dir`.
        /// </summary>
        /// <param name="tree_cache_folder"></param>
        /// <param name="commit_hash"></param>
        /// <param name="base_dir"></param>
        /// <param name="allow_patterns"></param>
        /// <param name="ignore_patterns"></param>
        /// <param name="repo_id"></param>
        /// <param name="revision"></param>
        /// <param name="api_call_error"></param>
        private static void _raise_if_incomplete_snapshot(string tree_cache_folder, string commit_hash, string base_dir,
            dynamic allow_patterns, dynamic ignore_patterns, string repo_id, string revision, Exception api_call_error)
        {
            var tree_entries = tree_cache.read_tree_cache(tree_cache_folder, commit_hash);

            if (tree_entries == null)
            {
                return;
            }

            IEnumerable<string> expected = paths.filter_repo_objects(items: tree_entries.Keys, allow_patterns: allow_patterns, ignore_patterns: ignore_patterns);

            var missing = expected.Where(item => !_local_file_exists(base_dir, item)).ToArray();

            if (missing == null || missing.Length == 0)
            {
                return;
            }

            var sample = string.Join(", ", missing.Take(3));

            if (missing.Length > 3)
            {
                sample += $", ... ({missing.Length - 3} more)";
            }

            string reason = null;

            if (api_call_error != null)
            {
                reason = $"The Hub could not be reached ({api_call_error?.Source}: {api_call_error})."; ////to do
            }
            else
            {
                reason = "Outgoing traffic is disabled ('local_files_only=True').";
            }

            throw new IncompleteSnapshotError($"The cached snapshot for '{repo_id}' (revision '{revision}', commit {commit_hash}) is incomplete: " +
                $"{missing.Length} file(s) are missing ({sample}). {reason} Re-run the download with network access " +
                "to complete the snapshot."
                );
        }

        /// <summary>
        /// Check whether a repo file (path relative to `base_dir`, '/'-separated) exists on disk.
        /// </summary>
        /// <param name="base_dir"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        private static bool _local_file_exists(string base_dir, string path)
        {
            return File.Exists(as_extended_path(Path.Combine(Enumerable.Concat([base_dir], path.Split('/')).ToArray())));
        }

        /// <summary>
        /// Return `path` in its Windows extended-length form if it is too long, unchanged otherwise.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="max_length"></param>
        /// <returns></returns>
        public static string as_extended_path(string path, int max_length = 255)
        {
            if (!FileHelper.IsNT())
            {
                return path;
            }

            var absolute_path = Path.GetFullPath(path);

            if (absolute_path.Length <= max_length || absolute_path.StartsWith("\\\\?\\"))
            {
                return path;
            }

            if (absolute_path.StartsWith("\\\\")) // UNC share: `\\server\share\...` => `\\?\UNC\server\share\...`
            {
                return "\\\\?\\UNC\\" + absolute_path.Substring(2);
            }

            return "\\\\?\\" + absolute_path;
        }
    }
}
