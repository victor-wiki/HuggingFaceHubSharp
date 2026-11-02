using HuggingFaceHubSharp.Model;
using HuggingFaceHubSharp.utils;
using Newtonsoft.Json;
using SharpUtility.Helper;
using System.Text.RegularExpressions;

namespace HuggingFaceHubSharp
{
    public class tree_cache
    {
        // In-memory cache of parsed tree listings, keyed by absolute file path.
        private static readonly Dictionary<string, Dictionary<string, TreeCacheEntry>> _IN_MEMORY_TREE_CACHE = new Dictionary<string, Dictionary<string, TreeCacheEntry>>();

        public const int TREE_CACHE_FORMAT_VERSION = 1;

        /// <summary>
        /// Folder under which the `trees/` cache lives for a `local_dir` download.
        /// </summary>
        /// <param name="local_dir"></param>
        /// <returns></returns>
        public static string tree_cache_folder_for_local_dir(string local_dir)
        {
            return Path.Combine(local_dir, ".cache", "huggingface");
        }

        /// <summary>
        /// Return the cached tree listing for a commit hash, or `null` if not cached, invalid, or unreadable.
        /// </summary>
        /// <returns></returns>
        public static Dictionary<string, TreeCacheEntry> read_tree_cache(string tree_cache_folder, string commit_hash)
        {
            var path = _tree_cache_path(tree_cache_folder, commit_hash);

            if (_IN_MEMORY_TREE_CACHE.ContainsKey(path))
            {
                var cached_entries = _IN_MEMORY_TREE_CACHE[path];

                return is_valid_tree_entries(cached_entries) ? cached_entries : null;
            }

            var entries = _read_tree_cache_from_disk(path);

            if (entries == null || !is_valid_tree_entries(entries))
            {
                return null;
            }

            _IN_MEMORY_TREE_CACHE[path] = entries;

            return entries;
        }

        private static Dictionary<string, TreeCacheEntry> _read_tree_cache_from_disk(string path)
        {
            try
            {
                string content = File.ReadAllText(path, System.Text.Encoding.UTF8);

                var data = JsonConvert.DeserializeObject<Dictionary<string, string>>(content);

                if (data.GetValueOrDefault("format_version") != TREE_CACHE_FORMAT_VERSION.ToString())
                {
                    // Unknown format (e.g. written by a newer version) => ignore and re-fetch.
                    return null;
                }

                Dictionary<string, TreeCacheEntry> result = new Dictionary<string, TreeCacheEntry>();

                var files = JsonConvert.DeserializeObject<Dictionary<string, string>>(data["files"]); ////to do

                foreach (var item in files)
                {
                    string file_path = item.Key;

                    TreeCacheEntry entry = JsonConvert.DeserializeObject<TreeCacheEntry>(item.Value);

                    result.Add(file_path, entry);
                }

                return result;
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        /// <summary>
        /// Return whether all Xet hashes in the tree listing are valid.
        /// </summary>
        /// <param name="entries"></param>
        /// <returns></returns>
        public static bool is_valid_tree_entries(Dictionary<string, TreeCacheEntry> entries)
        {
            return entries.Values.All(item => item.xet_hash == null || is_valid_xet_hash(item.xet_hash));
        }

        /// <summary>
        /// Return whether `xet_hash` looks like a real Xet content hash (64 hex chars).
        /// </summary>
        /// <param name="xet_hash"></param>
        /// <returns></returns>
        public static bool is_valid_xet_hash(string xet_hash)
        {
            return Regex.IsMatch(xet_hash, xet.REGEX_XET_HASH);
        }

        private static string _tree_cache_path(string tree_cache_folder, string commit_hash)
        {
            return Path.Combine(tree_cache_folder, "trees", $"{commit_hash}.json");
        }

        /// <summary>
        /// Write a valid tree listing to the cache (ignoring invalid entries and any failures).
        /// </summary>
        /// <param name="tree_cache_folder"></param>
        /// <param name="commit_hash"></param>
        /// <param name="entries"></param>
        public static void write_tree_cache(string tree_cache_folder, string commit_hash, Dictionary<string, TreeCacheEntry> entries)
        {
            if (!is_valid_tree_entries(entries))
            {
                return;
            }

            var path = _tree_cache_path(tree_cache_folder, commit_hash);

            var data = new Dictionary<string, dynamic>() { { "format_version", TREE_CACHE_FORMAT_VERSION }, { "files", new Dictionary<string, dynamic>() } };

            foreach (var file_path in entries.Keys.OrderBy(item => item))
            {
                data["files"].Add(file_path, ObjectHelper.ToJson(entries[file_path].ToString()));
            }

            try
            {
                FileHelper.MakeDir(Path.GetDirectoryName(path));

                string tmp_path = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + ".tmp");

                File.WriteAllText(tmp_path, JsonConvert.SerializeObject(data, Formatting.Indented), System.Text.Encoding.UTF8);

                File.Move(tmp_path, path, true);
            }
            catch (Exception ex)
            {
                LogHelper.LogWarnning($"Ignored error while writing tree cache file {path}: {ex.Message}");
                return;
            }

            _IN_MEMORY_TREE_CACHE[path] = entries.ToDictionary();
        }
    }
}
