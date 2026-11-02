using PyException.common;
using SharpUtility.Helper;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace HuggingFaceHubSharp
{
    public class shared_blobs
    {
        private const string _XET_HASH_REGEX = @"[0-9a-f]{64}";
        private static string _REPO_DIR_REGEX => $@"(?:{string.Join('|', constants.REPO_TYPES_MAPPING.Keys.OrderBy(item => item))})--.+";
        private static string _MARKER_TMP_REGEX => $@"{Regex.Escape(SHARED_BLOBS_MARKER_NAME)}\.[0-9a-f]{{8}}\.tmp";
        private const string _MANIFEST_SUFFIX = ".refs";
        public const string SHARED_BLOBS_LAYOUT_VERSION = "1";
        public const string SHARED_BLOBS_DIR_NAME = "blobs";
        public const string SHARED_BLOBS_MARKER_NAME = ".huggingface-shared-blobs";

        /// <summary>
        /// Return whether a usable store entry exists, without touching the cache.
        /// </summary>
        /// <param name="xet_hash"></param>
        /// <param name="cache_dir"></param>
        /// <param name="expected_size"></param>
        /// <returns></returns>
        public static bool has_shared_blob(string xet_hash, string cache_dir, long? expected_size)
        {
            if (!shared_blobs_enabled() || !Regex.IsMatch(xet_hash, _XET_HASH_REGEX) || !is_shared_blobs_dir(shared_blobs_dir(cache_dir)))
            {
                return false;
            }

            return _is_usable_store_entry(shared_blob_path(cache_dir, xet_hash), expected_size);
        }

        /// <summary>
        /// Return the store path for a given Xet file hash.
        /// </summary>
        /// <param name="cache_dir"></param>
        /// <param name="xet_hash"></param>
        /// <returns></returns>
        public static string shared_blob_path(string cache_dir, string xet_hash)
        {
            if (!Regex.IsMatch(xet_hash, _XET_HASH_REGEX))
            {
                throw new ValueError($"Invalid Xet file hash: '{xet_hash}'.");
            }

            return Path.Combine(shared_blobs_dir(cache_dir), xet_hash.Substring(0, 2), xet_hash);
        }

        /// <summary>
        /// Return the path of the shared blob store inside a cache directory.
        /// </summary>
        /// <param name="cache_dir"></param>
        /// <returns></returns>
        public static string shared_blobs_dir(string cache_dir)
        {
            return Path.Combine(cache_dir, SHARED_BLOBS_DIR_NAME);
        }

        /// <summary>
        /// Return whether `store_path` is a regular, readable payload of the expected size.
        /// </summary>
        /// <param name="store_path"></param>
        /// <param name="expected_size"></param>
        /// <returns></returns>
        private static bool _is_usable_store_entry(string store_path, long? expected_size)
        {
            if (expected_size is null)
            {
                // Size unknown (HEAD without a content length): the entry cannot be validated.
                return false;
            }

            if (!FileHelper.IsRegularFile(store_path))
            {
                return false;
            }

            long size = FileHelper.GetFileSize(store_path);

            if (size != expected_size)
            {
                LogHelper.LogWarnning($"Shared blob '{store_path}' has an unexpected size ({size} instead of {expected_size}). " +
                    "Not using it.");

                return false;
            }

            ////to do

            return true;
        }

        /// <summary>
        /// Return whether `path` is an owned, supported shared blob store.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static bool is_shared_blobs_dir(string path)
        {
            string store_dir = path;
            string marker_path = Path.Combine(store_dir, SHARED_BLOBS_MARKER_NAME);

            if (!_is_directory(store_dir) || !_is_regular_file(marker_path))
            {
                return false;
            }

            try
            {
                return File.ReadAllText(marker_path).Trim() == $"{SHARED_BLOBS_LAYOUT_VERSION}";
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private static bool _is_directory(string path)
        {
            //  // checks if path is a regular file without following symlinks
            return Directory.Exists(path); // to do
        }

        private static bool _is_regular_file(string path)
        {
            // checks if path is a regular file without following symlinks
            FileAttributes attrs = File.GetAttributes(path);

            return !attrs.HasFlag(FileAttributes.Directory) && !attrs.HasFlag(FileAttributes.ReparsePoint);
        }

        /// <summary>
        /// Return whether the shared blob store may be used.
        /// </summary>
        /// <returns></returns>
        public static bool shared_blobs_enabled()
        {
            return !constants.HF_HUB_DISABLE_SHARED_BLOBS && !constants.HF_HUB_DISABLE_XET;
        }

        /// <summary>
        /// Materialize `blobs/<etag>` as a symlink to an existing store entry, if any.
        /// 
        /// The reference manifest is flushed before the symlink becomes visible. Failures are
        /// best-effort misses and leave the regular download path untouched.
        /// </summary>
        /// <param name="blob_path"></param>
        /// <param name="xet_hash"></param>
        /// <param name="expected_size"></param>
        /// <returns></returns>
        public static bool try_link_from_shared_store(string blob_path, string xet_hash, string cache_dir, long? expected_size)
        {
            if (!Regex.IsMatch(xet_hash, _XET_HASH_REGEX) || !is_shared_blobs_dir(shared_blobs_dir(cache_dir)))
            {
                return false;
            }

            var store_path = shared_blob_path(cache_dir, xet_hash);
            var relative_blob_path = _relative_blob_path(blob_path, cache_dir);

            if (relative_blob_path is null)
            {
                return false;
            }

            string tmp_link = null;

            try
            {
                if (!_is_usable_store_entry(store_path, expected_size))
                {
                    return false;
                }

                tmp_link = _make_temporary_symlink(blob_path, store_path);

                _append_manifest_reference(_manifest_path_for_store_path(store_path), relative_blob_path);

                File.Move(tmp_link, blob_path, true);
            }
            catch (Exception ex)
            {
                return false;
            }
            finally
            {
                if (tmp_link is not null)
                {
                    File.Delete(tmp_link);
                }
            }

            return true;
        }

        /// <summary>
        /// Append and flush one reference before its symlink is made visible.
        /// </summary>
        /// <param name="manifest_path"></param>
        /// <param name="relative_blob_path"></param>
        private static void _append_manifest_reference(string manifest_path, string relative_blob_path)
        {
            if (!FileHelper.IsRegularFile(manifest_path))
            {
                throw new OSError($"Shared blob manifest is not a regular file: '{manifest_path}'.");
            }

            ////to do

            var line = $"{relative_blob_path}\n";

            File.AppendText(line);
        }

        private static string _manifest_path_for_store_path(string store_path)
        {
            return Path.Combine(Path.GetDirectoryName(store_path), $"{Path.GetFileNameWithoutExtension(store_path)}{_MANIFEST_SUFFIX}");
        }

        private static string _make_temporary_symlink(string blob_path, string store_path)
        {
            var tmp_link = Path.Combine(Path.GetDirectoryName(blob_path), $".{Path.GetFileNameWithoutExtension(blob_path)}.{RandomNumberGenerator.GetHexString(4)}.shared");
            var relative_target = Path.GetRelativePath(_path_for_comparison(store_path), Path.GetDirectoryName(_path_for_comparison(blob_path)));

            File.CreateSymbolicLink(tmp_link, relative_target);

            return tmp_link;
        }

        private static string _relative_blob_path(string blob_path, string cache_dir)
        {
            blob_path = _path_for_comparison(blob_path);
            cache_dir = _path_for_comparison(cache_dir);

            string relative_path = null;

            try
            {
                relative_path = Path.GetRelativePath(blob_path, cache_dir);
            }
            catch (Exception ex)
            {
                return null;
            }

            string[] parts = relative_path.Split('/', '\\');

            if (parts.Length != 3
                || !Regex.IsMatch(parts[0], _REPO_DIR_REGEX)
                || parts[1] != "blobs"
                || string.IsNullOrEmpty(parts[2]))
            {
                return null;
            }

            string relative_str = relative_path.Replace("\\", "/");

            return relative_str.Contains("\n") || relative_str.Contains("\n") ? null : relative_str;
        }

        /// <summary>
        /// Return an absolute path without the Windows extended-length prefix.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private static string _path_for_comparison(string path)
        {
            var path_str = path;

            if (path_str.Substring(0, 0).ToLower() == "\\\\?\\unc\\")
            {
                path_str = $"\\\\{path_str.Substring(8)}";
            }
            else if (path_str.StartsWith("\\\\?\\"))
            {
                path_str = path_str.Substring(4);
            }

            return Path.GetFullPath(path_str);
        }

        /// <summary>
        /// Move a fresh Xet download into the store and replace its repo blob with a symlink.
        /// 
        /// Best-effort: on failure the repo blob remains (or is restored as) a regular local
        /// file. Returns whether the repo blob was successfully shared.
        /// </summary>
        /// <param name="blob_path"></param>
        /// <param name="cache_dir"></param>
        /// <param name="expected_size"></param>
        /// <param name="replace_existing"></param>
        /// <returns></returns>
        public static bool publish_blob_to_shared_store(string blob_path, string xet_hash, string cache_dir, long? expected_size, bool replace_existing = false)
        {
            // Publishing a payload whose size is unknown would create an entry no reader can validate.
            if (expected_size is null || !Regex.IsMatch(xet_hash, _XET_HASH_REGEX))
            {
                return false;
            }

            var prefix_dir = _ensure_prefix_dir(cache_dir, xet_hash);
            var relative_blob_path = _relative_blob_path(blob_path, cache_dir);

            if (prefix_dir is null || relative_blob_path is null)
            {
                return false;
            }

            var blob_path_obj = blob_path;
            var store_path = shared_blob_path(cache_dir, xet_hash);
            var manifest_path = _manifest_path_for_store_path(store_path);
            string tmp_link = null;
            bool blob_moved = false;

            try
            {
                tmp_link = _make_temporary_symlink(blob_path_obj, store_path);
                var store_is_usable = !replace_existing && _is_usable_store_entry(store_path, expected_size);

                _append_manifest_reference(manifest_path, relative_blob_path);

                if (!store_is_usable)
                {
                    _prepare_shared_blob_permissions(blob_path_obj, prefix_dir, cache_dir);
                    File.Move(blob_path_obj, store_path, true);
                    blob_moved = true;
                }

                try
                {
                    File.Move(tmp_link, blob_path_obj, true);
                }
                catch (Exception ex)
                {
                    if (blob_moved)
                    {
                        File.Copy(store_path, blob_path_obj);  // restore under the lock, before GC can run
                        blob_moved = false;
                    }

                    throw ex;
                }
            }
            catch (Exception ex)
            {
                if (blob_moved)
                {
                    throw new OSError($"Could not restore repo blob '{blob_path}' after shared-store failure");
                }

                return false;
            }
            finally
            {
                if (tmp_link is not null)
                {
                    File.Delete(tmp_link);
                }
            }

            return true;
        }

        /// <summary>
        /// Make a payload immutable and readable according to the shared cache policy.
        /// </summary>
        /// <param name="blob_path"></param>
        /// <param name="prefix_dir"></param>
        /// <param name="cache_dir"></param>
        private static void _prepare_shared_blob_permissions(string blob_path, string prefix_dir, string cache_dir)
        {
            ////to do
        }

        private static string _ensure_prefix_dir(string cache_dir, string xet_hash)
        {
            if (!_ensure_shared_blobs_dir(cache_dir))
            {
                return null;
            }

            var prefix_dir = Path.GetDirectoryName(shared_blob_path(cache_dir, xet_hash));

            try
            {
                Directory.CreateDirectory(prefix_dir);
            }
            catch (Exception ex)
            {
                return null;
            }

            if (!_is_directory(prefix_dir))
            {
                return null;
            }

            try
            {
                _repair_shared_directory_mode(prefix_dir, cache_dir);
            }
            catch (Exception ex)
            {
                return null;
            }

            return prefix_dir;
        }

        /// <summary>
        /// Create and mark the shared store, refusing to adopt an unmarked directory.
        /// </summary>
        /// <param name="cache_dir"></param>
        /// <returns></returns>
        private static bool _ensure_shared_blobs_dir(string cache_dir)
        {
            var store_dir = shared_blobs_dir(cache_dir);
            var marker_path = Path.Combine(store_dir, SHARED_BLOBS_MARKER_NAME);

            try
            {
                Directory.CreateDirectory(store_dir);

                if (is_shared_blobs_dir(store_dir))
                {
                    return true;
                }

                if (!_is_directory(store_dir) || !_cleanup_abandoned_marker_temps(store_dir))
                {
                    return false;
                }

                _repair_shared_directory_mode(store_dir, cache_dir);

                var tmp_marker = Path.Combine(Path.GetDirectoryName(marker_path), $"{Path.GetFileNameWithoutExtension(marker_path)}.{RandomNumberGenerator.GetHexString(4)}.tmp");

                try
                {
                    File.WriteAllText(tmp_marker, $"{SHARED_BLOBS_LAYOUT_VERSION}\n");

                    //tmp_marker.chmod(_shared_blob_mode(cache_dir)); ////to do

                    File.Move(tmp_marker, marker_path, true);
                }
                finally
                {
                    File.Delete(tmp_marker);
                }

                return is_shared_blobs_dir(store_dir);
            }
            catch (Exception ex)
            {
                return false;
            }
        }

        private static void _repair_shared_directory_mode(string path, string cache_dir)
        {
            var expected_mode = _shared_directory_mode(cache_dir);

            ////to do
        }

        /// <summary>
        /// Mirror cache-root access and inheritance bits on newly created store directories.
        /// </summary>
        /// <param name="cache_dir"></param>
        private static int _shared_directory_mode(string cache_dir)
        {
            var mode = File.GetUnixFileMode(cache_dir);

            return (int)mode & (511 | 1024 | 512);
        }

        /// <summary>
        /// Remove leftover marker temporaries.
        /// </summary>
        /// <param name="store_dir"></param>
        /// <returns></returns>
        private static bool _cleanup_abandoned_marker_temps(string store_dir)
        {
            var expected_content = $"{SHARED_BLOBS_LAYOUT_VERSION}\n";
            var di = new DirectoryInfo(store_dir);
            var entries = new List<dynamic>();

            entries.AddRange(di.GetDirectories());
            entries.AddRange(di.GetFiles());

            foreach (var entry in entries)
            {
                if (!Regex.IsMatch(entry.Name, _MARKER_TMP_REGEX) || !_is_regular_file(entry.FullName))
                {
                    return false;
                }

                if (entry is FileInfo && !expected_content.StartsWith(File.ReadAllText((entry as FileInfo).FullName)))
                {
                    return false;
                }
            }

            foreach (var entry in entries)
            {
                if (entry is FileInfo f)
                {
                    f.Delete();
                }
                else if (entry is DirectoryInfo d)
                {
                    d.Delete();
                }
            }

            return !di.GetDirectories().Any() && !di.GetFiles().Any();
        }
    }
}
