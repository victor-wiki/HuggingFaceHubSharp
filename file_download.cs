using HuggingFaceHubSharp.errors;
using HuggingFaceHubSharp.Model;
using HuggingFaceHubSharp.utils;
using PyException.common;
using PyException.httpx;
using SharpUtility.Extension;
using SharpUtility.Helper;
using ShellProgressBar;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace HuggingFaceHubSharp
{
    public class file_download
    {
        private static Dictionary<string, bool> _are_symlinks_supported_in_dir = new Dictionary<string, bool>();

        public static string CACHED_NO_EXIST = "$_NO_EXIST_$";
        // Regex to check if the revision IS directly a commit_hash
        public const string REGEX_COMMIT_HASH = @"^[0-9a-f]{40}$";
        // Regex to check if the file etag IS a valid sha256
        public const string REGEX_SHA256 = @"^[0-9a-f]{64}$";
        // Regex to get filename from a "Content-Disposition" header for CDN-served files
        public const string HEADER_FILENAME_PATTERN = @"filename=""(?<filename>.*?)"";";

        /// <summary>
        ///  Explores the cache to return the latest cached file for a given revision if found.
        ///  This function will not raise any exception if the file in not cached.
        /// </summary>
        /// <param name="repo_id">The ID of the repo on huggingface.co.</param>
        /// <param name="filename">The filename to look for inside `repo_id`.</param>
        /// <param name="cache_dir">The folder where the cached files lie.</param>
        /// <param name="revision">The specific model version to use. Will default to `"main"` if it's not provided and no `commit_hash` is provided either.</param>
        /// <param name="repo_type">The type of the repository. Will default to `"model"`.</param>
        /// <returns>Will return `null` if the file was not cached. Otherwise:
        /// - The exact path to the cached file if it's found in the cache
        /// - A special value `_CACHED_NO_EXIST` if the file does not exist at the given commit hash and this fact was cached
        /// </returns>
        public static string try_to_load_from_cache(string repo_id, string filename, string cache_dir = null, string revision = null, string repo_type = null)
        {
            revision ??= "main";
            repo_type ??= "model";

            if (!constants.REPO_TYPES_WITH_KERNEL.Contains(repo_type))
            {
                throw new ValueError($"Invalid repo type: {repo_type}. Accepted repo types are: {constants.REPO_TYPES_WITH_KERNEL}");
            }

            cache_dir ??= constants.HF_HUB_CACHE;

            var repo_cache = Path.Combine(cache_dir, repo_folder_name(repo_id: repo_id, repo_type: repo_type));

            if (!Directory.Exists(repo_cache))
            {
                // No cache for this model
                return null;
            }

            var refs_dir = Path.Combine(repo_cache, "refs");
            var snapshots_dir = Path.Combine(repo_cache, "snapshots");
            var no_exist_dir = Path.Combine(repo_cache, ".no_exist");

            // Resolve refs (for instance to convert main to the associated commit sha)
            if (Directory.Exists(refs_dir))
            {
                var revision_file = Path.Combine(refs_dir, revision);

                if (File.Exists(revision_file))
                {
                    revision = File.ReadAllText(revision_file);
                }
            }

            // Check if file is cached as "no_exist"
            if (File.Exists(Path.Combine(no_exist_dir, revision, filename)))
            {
                return CACHED_NO_EXIST;
            }

            // Check if revision folder exists
            if (!Directory.Exists(snapshots_dir))
            {
                return null;
            }

            var cached_shas = new DirectoryInfo(snapshots_dir).GetDirectories().Select(item => item.FullName).ToArray(); //to do

            if (cached_shas.Contains(revision))
            {
                // No cache for this revision and we won't try to return a random revision
                return null;
            }

            // Check if file exists in cache
            var cached_file = Path.Combine(snapshots_dir, revision, filename);

            return File.Exists(cached_file) ? cached_file : null;
        }

        /// <summary>
        /// Return a serialized version of a hf.co repo name and type, safe for disk storage as a single non-nested folder.
        /// </summary>
        /// <param name="repo_id"></param>
        /// <param name="repo_type"></param>
        /// <returns></returns>
        public static string repo_folder_name(string repo_id, string repo_type)
        {
            // remove all `/` occurrences to correctly convert repo to directory name
            var parts = Enumerable.Concat([$"{repo_type}s"], repo_id.Split('/'));

            return string.Join(constants.REPO_ID_SEPARATOR, parts);
        }

        /// <summary>
        /// Download a given file if it's not already present in the local cache.
        /// The new cache file layout looks like this:
        /// - The cache directory contains one subfolder per repo_id (namespaced by repo type)
        /// - inside each repo folder:
        ///     - refs is a list of the latest known revision => commit_hash pairs
        ///     - blobs contains the actual file blobs (identified by their git-sha or sha256, depending on whether they're LFS files or not)
        ///     - snapshots contains one subfolder per commit, each "commit" contains the subset of the files that have been resolved at that particular commit. 
        ///       Each filename is a symlink to the blob at that particular commit.
        /// </summary>
        /// <param name="repo_id"></param>
        /// <param name="filename"></param>
        /// <param name="subfolder"></param>
        /// <param name="repo_type"></param>
        /// <param name="revision"></param>
        /// <param name="library_name"></param>
        /// <param name="library_version"></param>
        /// <param name="cache_dir"></param>
        /// <param name="local_dir"></param>
        /// <param name="user_agent"></param>
        /// <param name="force_download"></param>
        /// <param name="etag_timeout"></param>
        /// <param name="token"></param>
        /// <param name="local_files_only"></param>
        /// <param name="headers"></param>
        /// <param name="endpoint"></param>        
        /// <param name="dry_run"></param>
        /// <returns>- If `dry_run=False`: Local path of file or if networking is off, last version of file cached on disk.
        /// - If `dry_run=True`: A [`DryRunFileInfo`] object containing download information.
        /// </returns>
        public static async Task<dynamic> hf_hub_download(string repo_id,
                                            string filename,
                                            string subfolder = null,
                                            string repo_type = "model",
                                            dynamic revision = null,
                                            string library_name = null,
                                            string library_version = null,
                                            string cache_dir = null,
                                            string local_dir = null,
                                            dynamic user_agent = null,
                                            bool force_download = false,
                                            float etag_timeout = constants.DEFAULT_ETAG_TIMEOUT,
                                            dynamic token = null,
                                            bool local_files_only = false,
                                            Dictionary<string, string> headers = null,
                                            string endpoint = null,
                                            bool dry_run = false
            )
        {
            if (constants.HF_HUB_ETAG_TIMEOUT != constants.DEFAULT_ETAG_TIMEOUT)
            {
                // Respect environment variable above user value
                etag_timeout = constants.HF_HUB_ETAG_TIMEOUT;
            }

            if (revision is null)
            {
                revision = constants.DEFAULT_REVISION;
            }
            else if (revision is ResolvedRevision rr)
            {
                // Revision has already been resolved to a commit hash (see [`HfApi.resolve_revision`]) => use it directly.
                // This pins the download to an immutable commit and lets us skip network calls when it's already cached.
                revision = rr.resolved;
            }

            cache_dir ??= constants.HF_HUB_CACHE;
            cache_dir = PathHelper.ResolvePath(cache_dir);

            if (local_dir != null)
            {
                local_dir = PathHelper.ResolvePath(local_dir);
            }

            if (subfolder == "")
            {
                subfolder = null;
            }

            if (subfolder != null)
            {
                // This is used to create a URL, and not a local path, hence the forward slash.
                filename = $"{subfolder}/{filename}";
            }

            if (repo_type is null)
            {
                repo_type = "model";
            }

            if (!constants.REPO_TYPES_WITH_KERNEL.Contains(repo_type))
            {
                throw new ValueError($"Invalid repo type: {repo_type}. Accepted repo types are: {constants.REPO_TYPES_WITH_KERNEL.ToFridenlyString()}");
            }

            var hf_headers = await utils.headers.build_hf_headers(
                                token: token,
                                library_name: library_name,
                                library_version: library_version,
                                user_agent: user_agent,
                                headers: headers
                            );

            if (local_dir != null)
            {
                return await _hf_hub_download_to_local_dir(
                            // Destination
                            local_dir: local_dir,
                            // File info
                            repo_id: repo_id,
                            repo_type: repo_type,
                            filename: filename,
                            revision: revision,
                            // HTTP info
                            endpoint: endpoint,
                            etag_timeout: etag_timeout,
                            headers: hf_headers,
                            token: token,
                            // Additional options
                            cache_dir: cache_dir,
                            force_download: force_download,
                            local_files_only: local_files_only,
                            dry_run: dry_run
                        );
            }
            else
            {
                return await _hf_hub_download_to_cache_dir(
                            // Destination
                            cache_dir: cache_dir,
                            // File info
                            repo_id: repo_id,
                            filename: filename,
                            repo_type: repo_type,
                            revision: revision,
                            // HTTP info
                            endpoint: endpoint,
                            etag_timeout: etag_timeout,
                            headers: hf_headers,
                            token: token,
                            // Additional options
                            local_files_only: local_files_only,
                            force_download: force_download,
                            dry_run: dry_run
                        );
            }
        }

        /// <summary>
        /// Download a given file to a cache folder, if not already present.
        /// </summary>
        /// <param name="cache_dir"></param>
        /// <param name="repo_id"></param>
        /// <param name="filename"></param>
        /// <param name="repo_type"></param>
        /// <param name="revision"></param>
        /// <param name="endpoint"></param>
        /// <param name="etag_timeout"></param>
        /// <param name="headers"></param>
        /// <param name="token"></param>
        /// <param name="local_files_only"></param>
        /// <param name="force_download"></param>       
        /// <param name="dry_run"></param>
        /// <returns></returns>
        private static async Task<dynamic> _hf_hub_download_to_cache_dir(
                    // Destination
                    string cache_dir,
                    // File info
                    string repo_id,
                    string filename,
                    string repo_type,
                    string revision,
                    // HTTP info
                    string endpoint,
                    float etag_timeout,
                    Dictionary<string, string> headers,
                    dynamic token,
                    // Additional options
                    bool local_files_only,
                    bool force_download,
                    bool dry_run)
        {
            var locks_dir = Path.Combine(cache_dir, ".locks");
            var storage_folder = Path.Combine(cache_dir, repo_folder_name(repo_id: repo_id, repo_type: repo_type));

            local_folder._validate_relative_filename(filename);

            // cross-platform transcription of filename, to be used as a local file path.
            var relative_filename = Path.Combine(filename.Split('/'));

            string pointer_path = null;

            // if user provides a commit_hash and they already have the file on disk, shortcut everything.
            if (Regex.IsMatch(revision, REGEX_COMMIT_HASH))
            {
                pointer_path = _get_pointer_path(storage_folder, revision, relative_filename);

                if (Path.Exists(pointer_path))
                {
                    if (dry_run)
                    {
                        return new DryRunFileInfo()
                        {
                            commit_hash = revision,
                            file_size = new FileInfo(pointer_path).Length,
                            filename = filename,
                            is_cached = true,
                            local_path = pointer_path,
                            will_download = force_download,
                        };
                    }

                    if (!force_download)
                    {
                        return pointer_path;
                    }
                }
            }

            string url_to_download = null;
            string etag = null;
            string commit_hash = null;
            long? expected_size = null;
            XetFileData xet_file_data = null;
            Exception head_call_error = null;

            // Try to get metadata (etag, commit_hash, url, size) from the server.
            // If we can't, a HEAD request error is returned.
            Func<Task> get_metadata = async () =>
            {
                dynamic result = await _get_metadata_or_catch_error(repo_id: repo_id,
                    filename: filename,
                    repo_type: repo_type,
                    revision: revision,
                    endpoint: endpoint,
                    etag_timeout: etag_timeout,
                    headers: headers,
                    token: token,
                    local_files_only: local_files_only,
                    storage_folder: storage_folder,
                    relative_filename: relative_filename,
                    tree_cache_folder: storage_folder
                  );

                var items = TupleHelper.GetTupleItems(result);

                url_to_download = items[0];
                etag = items[1];
                commit_hash = items[2];
                expected_size = items[3];
                xet_file_data = items[4];
                head_call_error = items[5];
            };

            await get_metadata();

            // etag can be None for several reasons:
            // 1. we passed local_files_only.
            // 2. we don't have a connection
            // 3. Hub is down (HTTP 500, 503, 504)
            // 4. repo is not found -for example private or gated- and invalid/missing token sent
            // 5. Hub is blocked by a firewall or proxy is not set correctly.
            // => Try to get the last downloaded one from the specified revision.
            //
            // If the specified revision is a commit hash, look inside "snapshots".
            // If the specified revision is a branch or tag, look inside "refs".
            if (head_call_error is not null)
            {
                // Couldn't make a HEAD call => let's try to find a local file
                if (!force_download)
                {
                    commit_hash = null;

                    if (Regex.IsMatch(revision, REGEX_COMMIT_HASH))
                    {
                        commit_hash = revision;
                    }
                    else
                    {
                        var ref_path = Path.Combine(storage_folder, "refs", revision);

                        if (File.Exists(ref_path))
                        {
                            commit_hash = File.ReadAllText(ref_path);
                        }
                    }

                    // Return pointer file if exists
                    if (commit_hash is not null)
                    {
                        pointer_path = _get_pointer_path(storage_folder, commit_hash, relative_filename);

                        if (Path.Exists(pointer_path))
                        {
                            if (dry_run)
                            {
                                return new DryRunFileInfo()
                                {
                                    commit_hash = commit_hash,
                                    file_size = new FileInfo(pointer_path).Length,
                                    filename = filename,
                                    is_cached = true,
                                    local_path = pointer_path,
                                    will_download = force_download
                                };
                            }

                            if (!force_download)
                            {
                                return pointer_path;
                            }
                        }
                    }

                    if ((head_call_error is PyException.httpx.TimeoutException or NetworkError or RemoteProtocolError) ||
                        (head_call_error is HfHubHTTPError /*&& head_call_error.response.status_code in _DEFAULT_RETRY_ON_STATUS_CODES*/)) ////to do
                    {
                        LogHelper.LogInfo("No local file found. Retrying..");

                        await get_metadata();
                    }
                }

                // If still error, raise
                if (head_call_error is not null)
                {
                    _raise_on_head_call_error(head_call_error, force_download, local_files_only);
                }
            }

            // From now on, etag, commit_hash and url are not None.
            var blob_path = Path.Combine(storage_folder, "blobs", etag);
            pointer_path = _get_pointer_path(storage_folder, commit_hash, relative_filename);

            if (dry_run)
            {
                // A usable shared store entry counts as cached: the real call would symlink it without downloading.
                // Symlink support is not probed here (probing writes to the cache), so the preview may be optimistic.
                var is_cached = Path.Exists(pointer_path) || Path.Exists(blob_path) ||
                    (xet_file_data is not null && !constants.HF_HUB_DISABLE_SYMLINKS
                    && shared_blobs.has_shared_blob(xet_hash: xet_file_data.file_hash, cache_dir: cache_dir, expected_size: expected_size));

                return new DryRunFileInfo()
                {
                    commit_hash = commit_hash,
                    file_size = expected_size,
                    filename = filename,
                    is_cached = is_cached,
                    local_path = pointer_path,
                    will_download = force_download || !is_cached
                };
            }

            // Pointer already exists -> update the ref best-effort, then return without
            // attempting to write to the cache (which may be mounted read-only).
            if (!force_download && Path.Exists(pointer_path))
            {
                try
                {
                    _cache_commit_hash_for_specific_revision(storage_folder, revision, commit_hash);
                }
                catch (OSError ex)
                {
                }

                return pointer_path;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(blob_path));
            Directory.CreateDirectory(Path.GetDirectoryName(pointer_path));

            // Tag cache_dir so backup tools can skip it (CACHEDIR.TAG standard).
            local_folder._create_cachedir_tag(cache_dir);

            // if passed revision is not identical to commit_hash
            // then revision has to be a branch name or tag name.
            // In that case store a ref.
            _cache_commit_hash_for_specific_revision(storage_folder, revision, commit_hash);

            // Prevent parallel downloads of the same file with a lock.
            // etag could be duplicated across repos.
            // Note: the lock is best-effort to avoid downloading the same file twice. Cache correctness
            // does not depend on it: each download writes to a process-unique temporary file that is
            // atomically renamed into place (see `_download_to_tmp_and_move`).
            var lock_path = Path.Combine(locks_dir, repo_folder_name(repo_id: repo_id, repo_type: repo_type), $"{etag}.lock");

            lock_path = paths.as_extended_path(lock_path);
            blob_path = paths.as_extended_path(blob_path);

            FileHelper.MakeDir(Path.GetDirectoryName(lock_path));

            // Blob exists but pointer must be (safely) created -> take the lock
            if (!force_download && Path.Exists(blob_path))
            {
                if (!Path.Exists(pointer_path))
                {
                    _create_symlink(blob_path, pointer_path, new_blob: false);
                }

                return pointer_path;
            }

            // Local file doesn't exist or etag isn't a match => retrieve file from remote (or cache)

            // Xet hash of the blob in the shared store, None when the store must not be used for this download
            // (see `utils/_shared_blobs.py`).
            var shared_blob_hash = xet_file_data is not null && shared_blobs.shared_blobs_enabled() && are_symlinks_supported(cache_dir) ? xet_file_data.file_hash : null;

            using (WeakFileLock fileLock = new WeakFileLock(lock_path))
            {
                bool blob_reused_from_store = shared_blob_hash is not null && !force_download && !Path.Exists(blob_path)
                    && shared_blobs.try_link_from_shared_store(blob_path: blob_path, xet_hash: shared_blob_hash, cache_dir: cache_dir, expected_size: expected_size);

                var blob_is_shared = blob_reused_from_store;

                if (!blob_reused_from_store)
                {
                    var will_download = force_download || !Path.Exists(blob_path);

                    await _download_to_tmp_and_move(
                        incomplete_path: $"{blob_path}.incomplete",
                        destination_path: blob_path,
                        url_to_download: url_to_download,
                        headers: headers,
                        expected_size: expected_size,
                        filename: filename,
                        force_download: force_download,
                        etag: etag,
                        xet_file_data: xet_file_data
                    );

                    if (shared_blob_hash is not null && will_download && runtime.is_xet_available())
                    {
                        // Only Xet-verified downloads are published, never the plain HTTP fallback.
                        blob_is_shared = shared_blobs.publish_blob_to_shared_store(
                            blob_path: blob_path,
                            xet_hash: shared_blob_hash,
                            cache_dir: cache_dir,
                            expected_size: expected_size,
                            replace_existing: force_download
                        );
                    }
                }

                if (!Path.Exists(pointer_path))
                {
                    // A shared blob is a symlink: if the snapshot symlink fails, copy through it instead of moving it away.
                    _create_symlink(blob_path, pointer_path, new_blob: !blob_is_shared);
                }
            }

            return pointer_path;
        }

        /// <summary>
        /// Create a symbolic link named dst pointing to src.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dst"></param>
        /// <param name="new_blob"></param>
        private static void _create_symlink(string src, string dst, bool new_blob = false)
        {
            try
            {
                File.Delete(dst);
            }
            catch (Exception ex)
            {
            }

            var abs_src = Path.GetFullPath(Environment.ExpandEnvironmentVariables(src));
            var abs_dst = Path.GetFullPath(Environment.ExpandEnvironmentVariables(dst));
            var abs_dst_folder = Path.GetDirectoryName(abs_dst);

            string relative_src = null;

            // Use relative_dst in priority
            try
            {
                relative_src = Path.GetRelativePath(abs_dst_folder, abs_src);
            }
            catch (Exception ex)
            {
                // Raised on Windows if src and dst are not on the same volume. This is the case when creating a symlink to a
                // local_dir instead of within the cache directory.
                // See https://docs.python.org/3/library/os.path.html#os.path.relpath
                relative_src = null;
            }

            string commonpath = null;

            bool _support_symlinks = false;

            try
            {
                commonpath = PathHelper.GetCommonPath([abs_src, abs_dst]);
                _support_symlinks = are_symlinks_supported(commonpath);
            }
            catch (ValueError ve)
            {
                // Raised if src and dst are not on the same volume. Symlinks will still work on Linux/Macos.
                // See https://docs.python.org/3/library/os.path.html#os.path.commonpath
                _support_symlinks = !FileHelper.IsNT() && !constants.HF_HUB_DISABLE_SYMLINKS;
            }
            catch (PermissionError pe)
            {
                // Permission error means src and dst are not in the same volume (e.g. destination path has been provided
                // by the user via `local_dir`. Let's test symlink support there)
                _support_symlinks = are_symlinks_supported(abs_dst_folder);
            }
            catch (Exception ex)
            {
                throw ex;
            }

            // Symlinks are supported => let's create a symlink.
            if (_support_symlinks)
            {
                var src_rel_or_abs = relative_src ?? abs_src;

                try
                {
                    File.CreateSymbolicLink(abs_dst, src_rel_or_abs);

                    return;
                }
                catch (FileExistsError fee)
                {
                    if (FileHelper.IsSymlink(abs_dst) && PathHelper.GetFileRealPath(abs_dst) == PathHelper.GetFileRealPath(abs_src))
                    {
                        return;
                    }
                    else
                    {
                        throw fee;
                    }
                }
            }

            // Symlinks are not supported => let's move or copy the file.
            if (new_blob)
            {
                File.Move(abs_src, abs_dst, true);
            }
            else
            {
                File.Copy(abs_src, abs_dst, true);
            }
        }

        /// <summary>
        /// Return whether the symlinks are supported on the machine.
        /// </summary>
        /// <param name="cache_dir"></param>
        /// <returns></returns>
        public static bool are_symlinks_supported(string cache_dir)
        {
            // Defaults to HF cache
            if (cache_dir is null)
            {
                cache_dir = constants.HF_HUB_CACHE;
            }

            cache_dir = PathHelper.ResolvePath(cache_dir);  // make it unique

            // If symlinks are explicitly disabled by the user, always return False
            if (constants.HF_HUB_DISABLE_SYMLINKS)
            {
                return false;
            }

            // Check symlink compatibility only once (per cache directory) at first time use
            if (!_are_symlinks_supported_in_dir.ContainsKey(cache_dir))
            {
                _are_symlinks_supported_in_dir[cache_dir] = true;

                Directory.CreateDirectory(cache_dir);

                using (TemporaryDirectory td = new TemporaryDirectory(dir: cache_dir))
                {
                    var tmpdir = td.Path;

                    var src_path = Path.Combine(tmpdir, "dummy_file_src");

                    Directory.CreateDirectory(src_path);

                    var dst_path = Path.Combine(tmpdir, "dummy_file_dst");

                    // Relative source path as in `_create_symlink``
                    var relative_src = Path.GetRelativePath(Directory.GetParent(dst_path).FullName, src_path);

                    try
                    {
                        Directory.CreateSymbolicLink(dst_path, relative_src);
                    }
                    catch (Exception ex)
                    {
                        // Likely running on Windows
                        _are_symlinks_supported_in_dir[cache_dir] = false;

                        string message = "";

                        if (!constants.HF_HUB_DISABLE_SYMLINKS_WARNING)
                        {
                            message = "`huggingface_hub` cache-system uses symlinks by default to" +
                            " efficiently store duplicated files but your machine does not" +
                            $" support them in {cache_dir}. Caching files will still work" +
                            " but in a degraded version that might require more space on" +
                            " your disk. This warning can be disabled by setting the" +
                            " `HF_HUB_DISABLE_SYMLINKS_WARNING` environment variable. For" +
                            " more details, see" +
                            " https://huggingface.co/docs/huggingface_hub/how-to-cache#limitations.";
                        }

                        if (FileHelper.IsNT())
                        {
                            message += "\nTo support symlinks on Windows, you either need to" +
                            " activate Developer Mode or to run Python as an" +
                            " administrator. In order to activate developer mode," +
                            " see this article:" +
                            " https://docs.microsoft.com/en-us/windows/apps/get-started/enable-your-device-for-development";
                        }

                        LogHelper.LogWarnning(message);
                    }
                }
            }

            return _are_symlinks_supported_in_dir[cache_dir];
        }

        /// <summary>
        /// Cache reference between a revision (tag, branch or truncated commit hash) and the corresponding commit hash.
        /// 
        /// Does nothing if `revision` is already a proper `commit_hash` or reference is already cached.
        /// </summary>
        /// <param name="storage_folder"></param>
        /// <param name="revision"></param>
        /// <param name="commit_hash"></param>
        private static void _cache_commit_hash_for_specific_revision(string storage_folder, string revision, string commit_hash)
        {
            if (revision != commit_hash)
            {
                var ref_path = Path.Combine(storage_folder, "refs", revision);

                FileHelper.MakeDir(Path.GetDirectoryName(ref_path));

                if (!File.Exists(ref_path) || commit_hash != File.ReadAllText(ref_path))
                {
                    // Update ref only if has been updated. Could cause useless error in case
                    // repo is already cached and user doesn't have write access to cache folder.
                    // See https://github.com/huggingface/huggingface_hub/issues/1216.
                    // Write atomically (tmp file + rename) so that concurrent readers never see
                    // a partially written ref.
                    var tmp_path = Path.Combine(Path.GetDirectoryName(ref_path), $"{Path.GetFileNameWithoutExtension(ref_path)}.{Guid.NewGuid().ToString("N").Substring(0, 8)}.tmp");

                    File.WriteAllText(tmp_path, commit_hash);

                    File.Move(tmp_path, ref_path, true);
                }
            }
        }

        /// <summary>
        /// Download a given file to a local folder, if not already present.
        /// </summary>
        /// <param name="local_dir"></param>
        /// <param name="repo_id"></param>
        /// <param name="repo_type"></param>
        /// <param name="filename"></param>
        /// <param name="revision"></param>
        /// <param name="endpoint"></param>
        /// <param name="etag_timeout"></param>
        /// <param name="headers"></param>
        /// <param name="token"></param>
        /// <param name="cache_dir"></param>
        /// <param name="force_download"></param>
        /// <param name="local_files_only"></param>       
        /// <param name="dry_run"></param>
        /// <returns></returns>
        private static async Task<dynamic> _hf_hub_download_to_local_dir(
                                // Destination
                                string local_dir,
                                // File info
                                string repo_id,
                                string repo_type,
                                string filename,
                                string revision,
                                // HTTP info
                                string endpoint,
                                float etag_timeout,
                                Dictionary<string, string> headers,
                                dynamic token,
                                // Additional options
                                string cache_dir,
                                bool force_download,
                                bool local_files_only,
                                bool dry_run)
        {
            // Some Windows versions do not allow for paths longer than 255 characters.
            // In this case, we must specify it as an extended path by using the "\\?\" prefix.
            if (FileHelper.IsNT() && Path.GetFullPath(local_dir).Length > 255)
            {
                local_dir = "\\\\?\\" + Path.GetFullPath(local_dir);
            }

            var paths = local_folder.get_local_download_paths(local_dir: local_dir, filename: filename);
            var local_metadata = local_folder.read_download_metadata(local_dir: local_dir, filename: filename);

            // Local file exists + metadata exists + commit_hash matches => return file
            if (Regex.IsMatch(revision, REGEX_COMMIT_HASH)
                && File.Exists(paths.file_path)
                && local_metadata != null
                && local_metadata.commit_hash == revision)
            {
                var local_file = paths.file_path;

                if (dry_run)
                {
                    return new DryRunFileInfo()
                    {
                        commit_hash = revision,
                        file_size = new FileInfo(local_file).Length,
                        filename = filename,
                        is_cached = true,
                        local_path = local_file,
                        will_download = force_download
                    };
                }

                if (!force_download)
                {
                    return local_file;
                }

            }

            // Local file doesn't exist or commit_hash doesn't match => we need the etag
            // - Xet file => might be cached in /tree listing
            // - otherwise => HEAD /resolve call
            var tree_cache_folder = tree_cache.tree_cache_folder_for_local_dir(local_dir);

            var result = await _get_metadata_or_catch_error(
                                        repo_id: repo_id,
                                        filename: filename,
                                        repo_type: repo_type,
                                        revision: revision,
                                        endpoint: endpoint,
                                        etag_timeout: etag_timeout,
                                        headers: headers,
                                        token: token,
                                        local_files_only: local_files_only,
                                        tree_cache_folder: tree_cache_folder
                                    );

            var url_to_download = result.url_to_download;
            var etag = result.etag;
            var commit_hash = result.commit_hash;
            var expected_size = result.expected_size;
            var xet_file_data = result.xet_file_data;
            var head_call_error = result.head_call_error;

            if (head_call_error != null)
            {
                // No HEAD call but local file exists => default to local file
                if (File.Exists(paths.file_path))
                {
                    if (dry_run && !force_download)
                    {
                        LogHelper.LogWarnning($"Couldn't access the Hub to check for update but local file already exists. Defaulting to existing file. (error: {head_call_error})");
                    }

                    var local_path = paths.file_path;

                    if (dry_run && local_metadata != null)
                    {
                        return new DryRunFileInfo()
                        {
                            commit_hash = local_metadata.commit_hash,
                            file_size = new FileInfo(local_path).Length,
                            filename = filename,
                            is_cached = true,
                            local_path = local_path,
                            will_download = force_download,
                        };
                    }

                    if (!force_download)
                    {
                        return local_path;
                    }
                }
            }
            else if (!force_download)
            {
                ////to do
            }

            // If still error, raise
            if (head_call_error is not null)
            {
                _raise_on_head_call_error(head_call_error, force_download, local_files_only);
            }

            // Local file exists => check if it's up-to-date
            if (!force_download && File.Exists(paths.file_path))
            {
                // etag matches => update metadata and return file
                if (local_metadata != null && local_metadata.etag == etag)
                {
                    local_folder.write_download_metadata(local_dir: local_dir, filename: filename, commit_hash: commit_hash, etag: etag);

                    if (dry_run)
                    {
                        return new DryRunFileInfo()
                        {
                            commit_hash = commit_hash,
                            file_size = expected_size,
                            filename = filename,
                            is_cached = true,
                            local_path = paths.file_path,
                            will_download = false
                        };
                    }

                    return paths.file_path;
                }

                // metadata is outdated + etag is a sha256
                // => means it's an LFS file (large)
                // => let's compute local hash and compare
                // => if match, update metadata and return file
                if (local_metadata != null && Regex.IsMatch(etag, REGEX_SHA256))
                {
                    var file_hash = GetFileHash(paths.file_path);

                    if (file_hash == etag)
                    {
                        local_folder.write_download_metadata(local_dir: local_dir, filename: filename, commit_hash: commit_hash, etag: etag);

                        if (dry_run)
                        {
                            return new DryRunFileInfo()
                            {
                                commit_hash = commit_hash,
                                file_size = expected_size,
                                filename = filename,
                                is_cached = true,
                                local_path = paths.file_path,
                                will_download = false
                            };
                        }

                        return paths.file_path;
                    }
                }
            }

            // Local file doesn't exist or etag isn't a match => retrieve file from remote (or cache)

            //If we are lucky enough, the file is already in the cache => copy it
            if (!force_download)
            {
                var cached_path = try_to_load_from_cache(
                                        repo_id: repo_id,
                                        filename: filename,
                                        cache_dir: cache_dir,
                                        revision: commit_hash,
                                        repo_type: repo_type
                                    );

                if (dry_run)
                {
                    return new DryRunFileInfo()
                    {
                        commit_hash = commit_hash,
                        file_size = expected_size,
                        filename = filename,
                        is_cached = true,
                        local_path = paths.file_path,
                        will_download = false,
                    };
                }

                FileHelper.MakeDir(Path.GetDirectoryName(paths.file_path));
                File.Copy(cached_path, paths.file_path);

                local_folder.write_download_metadata(local_dir: local_dir, filename: filename, commit_hash: commit_hash, etag: etag);

                return paths.file_path;
            }

            if (dry_run)
            {
                var is_cached = File.Exists(paths.file_path);

                return new DryRunFileInfo()
                {
                    commit_hash = commit_hash,
                    file_size = expected_size,
                    filename = filename,
                    is_cached = is_cached,
                    local_path = paths.file_path,
                    will_download = force_download || !is_cached
                };
            }

            // Otherwise, let's download the file!
            if (File.Exists(paths.file_path))
            {
                File.Delete(paths.file_path);
            }

            await _download_to_tmp_and_move(
              incomplete_path: paths.incomplete_path(etag),
              destination_path: paths.file_path,
              url_to_download: url_to_download,
              headers: headers,
              expected_size: expected_size,
              filename: filename,
              force_download: force_download,
              etag: etag,
              xet_file_data: xet_file_data
          );

            local_folder.write_download_metadata(local_dir: local_dir, filename: filename, commit_hash: commit_hash, etag: etag);

            return paths.file_path;
        }

        /// <summary>
        /// Download content from a URL to a destination path
        /// </summary>
        /// <param name="incomplete_path"></param>
        /// <param name="destination_path"></param>
        /// <param name="url_to_download"></param>
        /// <param name="headers"></param>
        /// <param name="expected_size"></param>
        /// <param name="filename"></param>
        /// <param name="force_download"></param>
        /// <param name="etag"></param>
        /// <param name="xet_file_data"></param>       
        private static async Task _download_to_tmp_and_move(string incomplete_path,
                                    string destination_path,
                                    string url_to_download,
                                    Dictionary<string, string> headers,
                                    long? expected_size,
                                    string filename,
                                    bool force_download,
                                    string etag,
                                    XetFileData xet_file_data)
        {
            if (Path.Exists(destination_path) && !force_download)
            {
                return;
            }

            var tmp_path = Path.Combine(Path.GetDirectoryName(incomplete_path), $"{Path.GetFileNameWithoutExtension(incomplete_path)}.{Guid.NewGuid().ToString("N")[..8]}.incomplete");

            try
            {
                using (FileStream f = File.OpenWrite(tmp_path))
                {
                    LogHelper.LogInfo($"Downloading '{filename}' to '{tmp_path}'");

                    if (expected_size != null) //might be null if HTTP header not set correctly
                    {
                        // Check disk space in both tmp and destination path
                        _check_disk_space(expected_size.Value, Path.GetDirectoryName(tmp_path));
                        _check_disk_space(expected_size.Value, Path.GetDirectoryName(destination_path));
                    }

                    if (xet_file_data != null && runtime.is_xet_available())
                    {
                        LogHelper.LogInfo("Xet Storage is enabled for this repo. Downloading file from Xet Storage..");

                        xet_get(
                            incomplete_path: tmp_path,
                            xet_file_data: xet_file_data,
                            headers: headers,
                            expected_size: expected_size,
                            displayed_filename: filename
                        );
                    }
                    else
                    {
                        if (xet_file_data != null && !constants.HF_HUB_DISABLE_XET)
                        {
                            LogHelper.LogWarnning(
                                "Xet Storage is enabled for this repo, but the 'hf_xet' package is not installed. " + Environment.NewLine +
                                "Falling back to regular HTTP download. " + Environment.NewLine +
                                "For better performance, install the package with: `pip install huggingface_hub[hf_xet]` or `pip install hf_xet`"
                            );
                        }

                        await http_get(
                            url_to_download,
                            f,
                            headers: headers,
                            expected_size: expected_size
                        );
                    }

                    _chmod_and_move(tmp_path, destination_path);
                }
            }
            catch (Exception ex)
            {
                if (File.Exists(tmp_path))
                {
                    File.Delete(tmp_path);
                }
            }
        }

        /// <summary>
        /// Set correct permission before moving a blob from tmp directory to cache dir.
        /// </summary>
        /// <param name="src"></param>
        /// <param name="dst"></param>
        private static void _chmod_and_move(string src, string dst)
        {
            // Get umask by creating a temporary file in the folder containing the incomplete file.
            // We know this folder is writable since the incomplete file has just been written there.
            // Probing next to `dst` is not always possible: when downloading to a local dir, `dst` is the
            // final file location and its parents might not be writable (e.g. read-only root filesystem).
            // See https://github.com/huggingface/huggingface_hub/issues/4304.
            var tmp_file = Path.Combine(Path.GetDirectoryName(src), $"tmp_{Guid.NewGuid().ToString("N")}");

            try
            {
                FileHelper.CreateFile(tmp_file);

                //copy acl ////to do
            }
            catch (Exception ex)
            {
                LogHelper.LogWarnning($"Could not set the permissions on the file '{src}'. Error: {ex.Message}.\nContinuing without setting permissions.");
            }
            finally
            {
                try
                {
                    File.Delete(tmp_file);
                }
                catch (Exception ex)
                {

                }
            }

            File.Move(src, dst, true);
        }

        /// <summary>
        /// Download a remote file. Do not gobble up errors, and will return errors tailored to the Hugging Face Hub.
        /// </summary>
        /// <param name="url"></param>
        /// <param name="temp_file"></param>
        /// <param name="resume_size"></param>
        /// <param name="headers"></param>
        /// <param name="expected_size"></param>
        /// <param name="displayed_filename"></param>      
        /// <param name="_nb_retries"></param>
        public static async Task http_get(string url,
                                FileStream temp_file,
                                long resume_size = 0,
                                Dictionary<string, string> headers = null,
                                long? expected_size = null,
                                string displayed_filename = null,
                                int _nb_retries = 5
                               )
        {
            if (expected_size is not null && resume_size == expected_size)
            {
                // If the file is already fully downloaded, we don't need to download it again.
                return;
            }

            var initial_headers = headers;
            headers = headers.clone();

            if (resume_size > 0)
            {
                headers["Range"] = http._adjust_range_header(headers.GetValueOrDefault("Range"), resume_size);
            }
            else if (expected_size.HasValue && expected_size > constants.MAX_HTTP_DOWNLOAD_SIZE)
            {
                // Any files over 50GB will not be available through basic http requests.
                throw new ValueError("The file is too large to be downloaded using the regular download method. " +
                    " Install `hf_xet` with `pip install hf_xet` for xet-powered downloads.");
            }

            var new_resume_size = resume_size;

            try
            {
                var response = await http.http_stream_backoff(
                            method: "GET",
                            url: url,
                            headers: headers,
                            timeout: constants.HF_HUB_DOWNLOAD_TIMEOUT,
                            retry_on_exceptions: [],
                            retry_on_status_codes: [408, 429]
                        );

                await http.hf_raise_for_status(response);

                // If we requested a Range but got 200 back, the server ignored our Range header
                // (e.g. CloudFront with Accept-Encoding: gzip). Reset file to avoid corruption.
                if (resume_size > 0 && (int)response.StatusCode == 200)
                {
                    temp_file.Seek(0, SeekOrigin.Begin);

                    resume_size = 0;
                }

                long? total = _get_file_length_from_http_response(response);

                if (expected_size is null)
                {
                    expected_size = total;
                }
                else if (total is null)
                {
                    // Hub serves compressible text files (e.g. vocab.json) with `Content-Encoding: gzip` and
                    // `Transfer-Encoding: chunked`, so the response carries no `Content-Length`. Fall back to the caller's
                    // `expected_size` (always known from the metadata HEAD on the hf_hub path) so the progress bar, and any
                    // aggregating wrapper such as snapshot_download's `_AggregatedTqdm` — still sees the file size.
                    total = expected_size;
                }

                if (displayed_filename is null)
                {
                    displayed_filename = url;
                    var content_disposition = response.Headers.get("Content-Disposition");

                    if (content_disposition is not null)
                    {
                        var match = Regex.Match(content_disposition, HEADER_FILENAME_PATTERN);

                        if (match.Success)
                        {
                            // Means file is on CDN
                            displayed_filename = match.Groups["filename"].Value;
                        }
                    }
                }

                // Truncate filename if too long to display
                if (displayed_filename.Length > 40)
                {
                    displayed_filename = $"(…){string.Join("", displayed_filename.TakeLast(40))}";
                }

                new_resume_size = resume_size;

                long totoalBytes = total ?? expected_size ?? 0;               

                bool showDownloadProgressBar = constants.SHOW_DOWNLOAD_PROGRESS_BAR;

                ProgressBar progressBar = null;
                IProgress<float> progress = null;
                
                if(showDownloadProgressBar)
                {
                    var options = new ProgressBarOptions
                    {
                        ForegroundColor = ConsoleColor.White,
                        ForegroundColorDone = ConsoleColor.Green
                    };

                    progressBar = new ProgressBar((int)totoalBytes, "", options);
                    progress = progressBar.AsProgress<float>(); 
                }              

                using Stream contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

                byte[] buffer = new byte[8192];
                long totalBytesRead = 0L;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) > 0)
                {
                    await temp_file.WriteAsync(buffer, 0, bytesRead).ConfigureAwait(false);
                    totalBytesRead += bytesRead;

                    new_resume_size += bytesRead;

                    if(showDownloadProgressBar)
                    {
                        progress.Report((float)Math.Round(totalBytesRead * 1.0 / totoalBytes, 4));
                    }                    
                }

                if (progressBar != null)
                {
                    progressBar.Dispose();
                }
            }
            catch (Exception ex) //when (ex is ConnectError or PyException.httpx.TimeoutException or RemoteProtocolError)
            {
                // Retry transient failures both when opening the stream and while reading its body.
                if (_nb_retries <= 0)
                {
                    LogHelper.LogWarnning($"Error while downloading from {url}: {ex.Message}\nMax retries exceeded.");
                    throw ex;
                }

                LogHelper.LogWarnning($"Error while downloading from {url}: {ex.Message}\nTrying to resume download...");

                Thread.Sleep(1000);

                await http_get(
                    url: url,
                    temp_file: temp_file,
                    resume_size: new_resume_size,
                    headers: initial_headers,
                    expected_size: expected_size,
                    _nb_retries: _nb_retries - 1
                );

                return;
            }

            var actual_size = temp_file.Position;

            if (expected_size is not null && expected_size != actual_size)
            {
                var consistency_error_message = $"Consistency check failed: file should be of size {expected_size} but has size" +
                  $" {actual_size} ({displayed_filename}).\nThis is usually due to network issues while downloading the file." +
                  " Please retry with `force_download=True`.";

                throw new OSError(consistency_error_message);
            }

            if (temp_file != null)
            {
                try
                {
                    temp_file.Close();
                }
                catch (Exception ex)
                {
                }
            }
        }

        private static long? _get_file_length_from_http_response(HttpResponseMessage response)
        {
            // If HTTP response contains compressed body (e.g. gzip), the `Content-Length` header will
            // contain the length of the compressed body, not the uncompressed file size.
            // And at the start of transmission there's no way to know the uncompressed file size for gzip,
            // thus we return None in that case.
            var content_encoding = response.Headers.get("Content-Encoding", "identity").ToLower();

            if (content_encoding != "identity")
            {
                // gzip/br/deflate/zstd etc
                return null;
            }

            var content_range = response.Headers.get("Content-Range");

            if (content_range is not null)
            {
                return long.Parse(content_range.Split('/').LastOrDefault());
            }

            var content_length = response.Headers.get("Content-Length");

            if (content_length is not null)
            {
                return long.Parse(content_length);
            }

            return response.Content.Headers.ContentLength;
        }

        /// <summary>
        /// Download a file using Xet storage service.
        /// </summary>
        /// <param name="incomplete_path"></param>
        /// <param name="xet_file_data"></param>
        /// <param name="headers"></param>
        /// <param name="expected_size"></param>
        /// <param name="displayed_filename"></param>        
        public static void xet_get(string incomplete_path,
                    XetFileData xet_file_data,
                    Dictionary<string, string> headers,
                    long? expected_size,
                    string displayed_filename = null
                   )
        {
            ////to do
        }

        /// <summary>
        /// Check disk usage and log a warning if there is not enough disk space to download the file.
        /// </summary>
        /// <param name="expected_size"></param>
        /// <param name="target_dir"></param>
        private static void _check_disk_space(long expected_size, string target_dir)
        {
            var drive = Path.GetPathRoot(target_dir);

            try
            {
                var di = new DriveInfo(drive);
                var target_dir_free = di.AvailableFreeSpace;

                if (target_dir_free < expected_size)
                {
                    LogHelper.LogWarnning("Not enough free disk space to download the file. " +
                        $"The expected file size is: {expected_size / 1e6:.2f} MB. " +
                        $"The target location {target_dir} only has {target_dir_free / 1e6:.2f} MB free disk space.");
                }
            }
            catch (Exception ex)
            {

            }
        }

        private static string GetFileHash(string filePath)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(File.ReadAllText(filePath));
            byte[] hash = sha256.ComputeHash(bytes);
            var file_hash = Convert.ToHexString(hash).ToLower();

            return file_hash;
        }

        /// <summary>
        /// Get metadata for a file on the Hub, safely handling network issues.
        /// </summary>
        /// <param name="repo_id"></param>
        /// <param name="filename"></param>
        /// <param name="repo_type"></param>
        /// <param name="revision"></param>
        /// <param name="endpoint"></param>
        /// <param name="etag_timeout"></param>
        /// <param name="headers"></param>
        /// <param name="token"></param>
        /// <param name="local_files_only"></param>
        /// <param name="relative_filename"></param>
        /// <param name="storage_folder"></param>
        /// <param name="retry_on_errors"></param>
        /// <param name="tree_cache_folder"></param>
        /// <returns>Returns either the etag, commit_hash and expected size of the file, or the error
        /// raised while fetching the metadata.
        /// </returns>
        private static async Task<(string url_to_download, string etag, string commit_hash, long? expected_size, XetFileData xet_file_data, Exception head_call_error)?> _get_metadata_or_catch_error(
                                string repo_id,
                                string filename,
                                string repo_type,
                                string revision,
                                string endpoint,
                                float etag_timeout,
                                Dictionary<string, string> headers,  // mutated inplace!
                                dynamic token,
                                bool local_files_only,
                                string relative_filename = null,  // only used to store `.no_exists` in cache
                                string storage_folder = null,  // only used to store `.no_exists` in cache
                                bool retry_on_errors = false,
                                string tree_cache_folder = null)  // if set, read the on-disk tree listing to skip the HEAD call
        {
            if (local_files_only)
            {
                return (null, null, null, null, null, new Exception($"Cannot access file since 'local_files_only=True' as been set. (repo_id: {repo_id}, repo_type: {repo_type}, revision: {revision}, filename: {filename})"));
            }

            // Skip the per-file HEAD call when the file metadata can be rebuilt from a tree listing cached on disk.
            if (tree_cache_folder != null && Regex.IsMatch(revision, REGEX_COMMIT_HASH))
            {
                var tree_metadata = _xet_file_metadata_from_tree_cache(
                                    tree_cache_folder: tree_cache_folder,
                                    repo_id: repo_id,
                                    repo_type: repo_type,
                                    commit_hash: revision,
                                    filename: filename,
                                    endpoint: endpoint
                                  );

                if (tree_metadata != null)
                {
                    return tree_metadata;
                }
            }

            var url = hf_hub_url(repo_id, filename, repo_type: repo_type, revision: revision, endpoint: endpoint);

            string url_to_download = url;
            string etag = null;
            string commit_hash = null;
            long? expected_size = null;
            Exception head_error_call = null;
            XetFileData xet_file_data = null;

            // Try to get metadata from the server.
            // Do not raise yet if the file is not found or not accessible.
            if (!local_files_only)
            {
                try
                {
                    HfFileMetadata metadata = null;

                    try
                    {
                        metadata = await get_hf_file_metadata(
                                    url: url,
                                    timeout: etag_timeout,
                                    headers: headers,
                                    token: token,
                                    endpoint: endpoint,
                                    retry_on_errors: retry_on_errors
                                );
                    }
                    catch (RemoteEntryNotFoundError http_error)
                    {
                        if (storage_folder is not null && relative_filename is not null)
                        {
                            // Cache the non-existence of the file
                            commit_hash = http_error.Response.Headers.get(constants.HUGGINGFACE_HEADER_X_REPO_COMMIT);

                            if (commit_hash is not null)
                            {
                                var no_exist_file_path = Path.Combine(storage_folder, ".no_exist", commit_hash, relative_filename);

                                if (!File.Exists(no_exist_file_path))
                                {
                                    try
                                    {
                                        FileHelper.MakeDir(Path.GetDirectoryName(no_exist_file_path));

                                        FileHelper.CreateFile(no_exist_file_path);
                                    }
                                    catch (Exception ex)
                                    {
                                        LogHelper.LogError($"Could not cache non-existence of file. Will ignore error and continue. Error: {ex.Message}");
                                    }
                                }

                                _cache_commit_hash_for_specific_revision(storage_folder, revision, commit_hash);
                            }
                        }

                        throw http_error;
                    }

                    // Commit hash must exist
                    commit_hash = metadata.commit_hash;

                    if (commit_hash is null)
                    {
                        throw new FileMetadataError($"Response from {url} is missing the '{constants.HUGGINGFACE_HEADER_X_REPO_COMMIT}' header, so it" +
                            " does not seem to be served by a Hugging Face Hub endpoint. If HF_ENDPOINT is set, check that it" +
                            " points to a Hub-compatible endpoint. Otherwise, check your firewall and proxy settings and make" +
                            " sure your SSL certificates are updated.");
                    }

                    // Etag must exist
                    // If we don't have any of those, raise an error.
                    etag = metadata.etag;

                    if (etag is null)
                    {
                        throw new FileMetadataError("Distant resource does not have an ETag, we won't be able to reliably ensure reproducibility.");
                    }

                    // Xet downloads require a known size, but regular HTTP downloads can recover it from the GET response.
                    expected_size = metadata.size;
                    xet_file_data = metadata.xet_file_data;

                    if (expected_size is null && xet_file_data is not null && runtime.is_xet_available())
                    {
                        throw new FileMetadataError("Distant resource does not have a Content-Length.");
                    }

                    // In case of a redirect, save an extra redirect on the request.get call,
                    // and ensure we download the exact atomic version even if it changed
                    // between the HEAD and the GET (unlikely, but hey).
                    //
                    // If the final location is on a Hub host (same host, or e.g. huggingface.co reached through an
                    // HF_ENDPOINT mirror redirect) => keep auth. Otherwise it's a signed CDN url => don't send auth.
                    if (xet_file_data is null && url != metadata.location)
                    {
                        url_to_download = metadata.location;

                        if (!http._is_same_or_hub_host(url, metadata.location))
                        {
                            // Remove authorization header when downloading a LFS blob from a CDN
                            headers.pop("authorization", null);
                        }
                    }
                }
                catch (ProxyError ex)
                {
                    throw ex;
                }
                catch (Exception ex) when (ex is ConnectError or PyException.httpx.TimeoutException or OfflineModeIsEnabled)
                {
                    // Otherwise, our Internet connection is down.
                    // etag is None
                    head_error_call = ex;
                }
                catch (Exception ex) when (ex is RevisionNotFoundError or RemoteEntryNotFoundError)
                {
                    // The repo was found but the revision or entry doesn't exist on the Hub (never existed or got deleted)
                    throw ex;
                }
                catch (HfHubHTTPError ex)
                {
                    // Multiple reasons for an http error:
                    // - Repository is private and invalid/missing token sent
                    // - Repository is gated and invalid/missing token sent
                    // - Hub is down (error 500 or 504)
                    // => let's switch to 'local_files_only=True' to check if the files are already cached.
                    //    (if it's not the case, the error will be re-raised)
                    head_error_call = ex;
                }
                catch (FileMetadataError ex)
                {
                    // Multiple reasons for a FileMetadataError:
                    // - Wrong network configuration (proxy, firewall, SSL certificates)
                    // - Inconsistency on the Hub
                    // => let's switch to 'local_files_only=True' to check if the files are already cached.
                    //    (if it's not the case, the error will be re-raised)
                    head_error_call = ex;
                }
            }

            if (!(local_files_only || etag != null || head_error_call != null))
            {
                throw new RuntimeError("etag is empty due to uncovered problems");
            }

            if (head_error_call != null)
            {
                _detach_tracebacks(head_error_call);
            }

            return (url_to_download, etag, commit_hash, expected_size, xet_file_data, head_error_call);  // type: ignore
        }

        /// <summary>
        /// Detach the tracebacks of an exception and of its whole `__cause__`/`__context__` chain.
        /// </summary>
        /// <param name="error"></param>
        private static void _detach_tracebacks(Exception error)
        {
            ////to do
        }

        public static async Task<HfFileMetadata> get_hf_file_metadata(string url,
                                    dynamic token = null,
                                    float? timeout = null,
                                    string library_name = null,
                                    string library_version = null,
                                    dynamic user_agent = null,
                                    Dictionary<string, string> headers = null,
                                    string endpoint = null,
                                    bool retry_on_errors = false)
        {
            timeout ??= constants.HF_HUB_ETAG_TIMEOUT;

            var hf_headers = await utils.headers.build_hf_headers(
                               token: token,
                               library_name: library_name,
                               library_version: library_version,
                               user_agent: user_agent,
                               headers: headers
                           );

            hf_headers["Accept-Encoding"] = "identity"; // prevent any compression => we want to know the real size of the file

            // Retrieve metadata
            HttpResponseMessage response = await http.httpx_follow_hub_redirects_with_backoff(method: "HEAD", url: url, headers: hf_headers, timeout: timeout, retry_on_errors: retry_on_errors);

            await http.hf_raise_for_status(response);

            if (response != null)
            {
                var responseHeaders = response.Headers;

                var strSize = responseHeaders.get(constants.HUGGINGFACE_HEADER_X_LINKED_SIZE) ?? (http.is_redirect(response) ? null : response.Content?.Headers?.ContentLength?.ToString());

                return new HfFileMetadata()
                {
                    commit_hash = responseHeaders.get(constants.HUGGINGFACE_HEADER_X_REPO_COMMIT),
                    // We favor a custom header indicating the etag of the linked resource, and we fall back to the regular etag header.
                    etag = _normalize_etag(responseHeaders.get(constants.HUGGINGFACE_HEADER_X_LINKED_ETAG) ?? responseHeaders.get("ETag")),
                    // Either from response headers (if redirected) or defaults to request url
                    // Do not use directly `url` as we might have followed relative redirects.
                    location = responseHeaders.get("Location") ?? response.RequestMessage.RequestUri.OriginalString,  // type: ignore
                    size = string.IsNullOrEmpty(strSize) ? null : long.Parse(strSize),
                    xet_file_data = xet.parse_xet_file_data_from_response(response, endpoint: endpoint)  // type: ignore    
                };
            }

            return null;
        }

        /// <summary>
        /// Normalize ETag HTTP header, so it can be used to create nice filepaths.
        /// </summary>
        /// <param name="etag"></param>
        /// <returns></returns>
        private static string _normalize_etag(string etag)
        {
            if (etag == null)
            {
                return null;
            }

            return etag.TrimStart('W', '/').Trim('"');
        }

        /// <summary>
        /// Rebuild the metadata a HEAD call would return, from the on-disk tree listing cache.
        /// </summary>
        /// <param name="tree_cache_folder"></param>
        /// <param name="repo_id"></param>
        /// <param name="repo_type"></param>
        /// <param name="commit_hash"></param>
        /// <param name="filename"></param>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        private static (string, string, string, long?, XetFileData, Exception)? _xet_file_metadata_from_tree_cache(string tree_cache_folder,
                                            string repo_id,
                                            string repo_type,
                                            string commit_hash,
                                            string filename,
                                            string endpoint)
        {
            if (!runtime.is_xet_available())
            {
                return null;
            }

            var tree_entries = tree_cache.read_tree_cache(tree_cache_folder, commit_hash);

            if (tree_entries is null)
            {
                return null;
            }

            var entry = tree_entries.GetValueOrDefault(filename);

            if (entry == null
                || entry.xet_hash == null
                || !xet.is_valid_xet_hash(entry.xet_hash)
                || entry.lfs_sha256 == null
                || entry.lfs_size == null)
            {
                return null;
            }

            var xet_file_data = new XetFileData()
            {
                file_hash = entry.xet_hash,
                refresh_route = xet.xet_connection_info_refresh_url(
                                    token_type: XetTokenType.READ,
                                    repo_id: repo_id,
                                    repo_type: repo_type,
                                    revision: commit_hash,
                                    endpoint: endpoint)
            };

            // Mirror the server's HEAD response: ETag/size come from the LFS metadata for LFS-tracked files (which Xet
            // files are).
            var etag = entry.lfs_sha256;
            var file_size = entry.lfs_size;

            var location = hf_hub_url(repo_id, filename, repo_type: repo_type, revision: commit_hash, endpoint: endpoint);

            return (location, etag, commit_hash, file_size, xet_file_data, null);
        }


        /// <summary>
        /// Construct the URL of a file from the given information.
        /// </summary>
        /// <param name=""></param>
        /// <param name="filename"></param>
        /// <param name="subfolder"></param>
        /// <param name="repo_type"></param>
        /// <param name="revision"></param>
        /// <param name="endpoint"></param>
        /// <returns></returns>
        public static string hf_hub_url(string repo_id,
                            string filename,
                            string subfolder = null,
                            string repo_type = null,
                            string revision = null,
                            string endpoint = null)
        {
            if (subfolder == "")
            {
                subfolder = null;
            }

            if (subfolder != null)
            {
                filename = $"{subfolder}/{filename}";
            }

            if (!constants.REPO_TYPES_WITH_KERNEL.Contains(repo_type))
            {
                throw new ValueError("Invalid repo type");
            }

            if (repo_type is not null && constants.REPO_TYPES_URL_PREFIXES.ContainsKey(repo_type))
            {
                repo_id = constants.REPO_TYPES_URL_PREFIXES[repo_type] + repo_id;  // type: ignore
            }

            if (revision == null)
            {
                revision = constants.DEFAULT_REVISION;
            }

            var url = constants.HUGGINGFACE_CO_URL_TEMPLATE.Replace("{repo_id}", repo_id).Replace("{revision}", HttpUtility.UrlEncode(revision)).Replace("{filename}", HttpUtility.UrlEncode(filename));

            // Update endpoint if provided
            if (endpoint != null && url.StartsWith(constants.ENDPOINT))
            {
                url = endpoint + url.Substring(constants.ENDPOINT.Length);
            }

            return url;
        }

        /// <summary>
        /// Cache reference between a revision (tag, branch or truncated commit hash) and the corresponding commit hash.
        /// 
        /// Does nothing if `revision` is already a proper `commit_hash` or reference is already cached.
        /// </summary>
        /// <param name="storage_folder"></param>
        /// <param name="revision"></param>
        /// <param name="commit_hash"></param>
        public static void cache_commit_hash_for_specific_revision(string storage_folder, string revision, string commit_hash)
        {
            if (revision != commit_hash)
            {
                var ref_path = Path.Combine(storage_folder, "refs", revision);

                FileHelper.MakeDir(Path.GetDirectoryName(ref_path));

                if (!File.Exists(ref_path) || commit_hash != File.ReadAllText(ref_path))
                {
                    // Update ref only if has been updated. Could cause useless error in case
                    // repo is already cached and user doesn't have write access to cache folder.
                    // See https://github.com/huggingface/huggingface_hub/issues/1216.
                    // Write atomically (tmp file + rename) so that concurrent readers never see
                    // a partially written ref.
                    var tmp_path = Path.Combine(Path.GetDirectoryName(ref_path), $"{Path.GetFileNameWithoutExtension(ref_path)}.{Guid.NewGuid().ToString().Substring(0, 8)}.tmp");

                    File.WriteAllText(tmp_path, commit_hash);

                    File.Move(tmp_path, ref_path, true);
                }
            }
        }

        private static string _get_pointer_path(string storage_folder, string revision, string relative_filename)
        {
            // Using `os.path.abspath` instead of `Path.resolve()` to avoid resolving symlinks
            var snapshot_path = Path.Combine(storage_folder, "snapshots");
            var pointer_path = Path.Combine(snapshot_path, revision, relative_filename);

            if (!PathHelper.GetParents(pointer_path).Contains(Path.GetFullPath(snapshot_path)))
            {
                throw new ValueError("Invalid pointer path: cannot create pointer path in snapshot folder if" +
                    $" `storage_folder='{storage_folder}'`, `revision='{revision}'` and" +
                    $" `relative_filename='{relative_filename}'`.");
            }

            return pointer_path;
        }

        /// <summary>
        /// Raise an appropriate error when the HEAD call failed and we cannot locate a local file.
        /// </summary>
        /// <param name="head_call_error"></param>
        /// <param name="force_download"></param>
        /// <param name="local_files_only"></param>
        private static void _raise_on_head_call_error(Exception head_call_error, bool force_download, bool local_files_only)
        {
            // No head call => we cannot force download.
            if (force_download)
            {
                if (local_files_only)
                {
                    throw new ValueError("Cannot pass 'force_download=True' and 'local_files_only=True' at the same time.");
                }
                else if (head_call_error is OfflineModeIsEnabled)
                {
                    throw new ValueError("Cannot pass 'force_download=True' when offline mode is enabled.");
                }
                else
                {
                    throw new ValueError("Force download failed due to the above error.");
                }
            }

            // No head call + couldn't find an appropriate file on disk => raise an error.
            if (local_files_only)
            {
                throw new LocalEntryNotFoundError("Cannot find the requested files in the disk cache and outgoing traffic has been disabled. To enable" +
                    " hf.co look-ups and downloads online, set 'local_files_only' to False.");
            }
            else if ((head_call_error is RepositoryNotFoundError or GatedRepoError) ||
                (head_call_error is HfHubHTTPError) && (int)(head_call_error as HfHubHTTPError).Response.StatusCode == 401)
            {
                // Repo not found or gated => let's raise the actual error
                // Unauthorized => likely a token issue => let's raise the actual error
                throw head_call_error;
            }
            else if (head_call_error is FileMetadataError)
            {
                // The call succeeded but the response lacked the metadata we need => a configuration issue, not connectivity.
                throw new LocalEntryNotFoundError("An error happened while trying to locate the file on the Hub and we cannot find the requested files" +
                    " in the local cache. Please check your connection and try again or make sure your Internet connection" +
                    " is on.");
            }

        }
    }
}
