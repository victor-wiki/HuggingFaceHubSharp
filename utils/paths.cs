using PyException.common;
using SharpUtility.Helper;

namespace HuggingFaceHubSharp.utils
{
    public class paths
    {
        public static IEnumerable<T> filter_repo_objects<T>(IEnumerable<T> items, dynamic allow_patterns = null, dynamic ignore_patterns = null, Func<T, string> key = null)
        {
            if (allow_patterns is string str1)
            {
                allow_patterns = new List<string>() { str1 };
            }

            if (ignore_patterns is string str2)
            {
                ignore_patterns = new List<string>() { str2 };
            }

            if (allow_patterns != null)
            {
                allow_patterns = (allow_patterns as List<string>).Select(item => _add_wildcard_to_directories(_normalize_separators(item))).ToList();
            }

            if (ignore_patterns != null)
            {
                ignore_patterns = (ignore_patterns as List<string>).Select(item => _add_wildcard_to_directories(_normalize_separators(item))).ToList();
            }

            if (key == null)
            {
                Func<T, string> _identity = (item) =>
                {
                    if (item is string str)
                    {
                        return str;
                    }

                    throw new ValueError($"Please provide `key` argument in `filter_repo_objects`: `{item}` is not a string.");
                };

                key = _identity;  // Items must be `str` or `Path`, otherwise raise ValueError
            }

            foreach (var item in items)
            {
                var path = _normalize_separators(key(item));

                // Skip if there's an allowlist and path doesn't match any
                if (allow_patterns != null && (allow_patterns as List<string>).Any(item => StringHelper.FnmatchCase(path, item)))
                {
                    continue;
                }

                // Skip if there's a denylist and path matches any
                if (ignore_patterns != null && (ignore_patterns as List<string>).Any(item => StringHelper.FnmatchCase(path, item)))
                {
                    continue;
                }

                yield return item;
            }
        }

        private static string _add_wildcard_to_directories(string pattern)
        {
            if (pattern.EndsWith("/"))
            {
                return pattern + "*";
            }

            return pattern;
        }

        private static string _normalize_separators(string value)
        {
            // Repo paths always use `/` and `\` is not an fnmatch escape character, so treating backslashes as path separators
            // (e.g. patterns built with `os.path.join` on Windows) is safe.
            // `value` can be a `Path` if a custom `key` returns one; coerce to `str` first.
            return value.Replace("\\", "/");
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

            if (absolute_path.StartsWith("\\\\")) // UNC share: `\\server\share\...` => `\\?\UNC\server\share\...`)
            {
                return "\\\\?\\UNC\\" + absolute_path.Substring(2);
            }

            return "\\\\?\\" + absolute_path;
        }
    }
}
