using HuggingFaceHubSharp.Model;
using PyException.common;
using SharpUtility.Extension;
using SharpUtility.Helper;

namespace HuggingFaceHubSharp
{
    public class local_folder
    {
        public static readonly string CACHEDIR_TAG_CONTENT =
"Signature: 8a477f597d28d172789f06886806bc55" + Environment.NewLine
+ "# This file is a cache directory tag created by huggingface_hub." + Environment.NewLine
+ "# For information about cache directory tags, see:" + Environment.NewLine
+ "#\thttps://bford.info/cachedir/" + Environment.NewLine;


        /// <summary>
        /// Compute paths to the files related to a download process.
        /// </summary>
        /// <param name="local_dir"></param>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static LocalDownloadFilePaths get_local_download_paths(string local_dir, string filename)
        {
            _validate_relative_filename(filename);

            // filename is the path in the Hub repository (separated by '/')
            // make sure to have a cross-platform transcription
            var sanitized_filename = Path.Combine(filename.Split('/'));
            var file_path = Path.Combine(local_dir, sanitized_filename);

            var metadata_path = Path.Combine(_huggingface_dir(local_dir), "download", $"{sanitized_filename}.metadata");
            var lock_path = Path.ChangeExtension(metadata_path, ".lock");

            // Some Windows versions do not allow for paths longer than 255 characters.
            // In this case, we must specify it as an extended path by using the "\\?\" prefix

            if (FileHelper.IsNT())
            {
                if (!local_dir.StartsWith("\\\\?\\") && Path.GetFullPath(lock_path).Length > 255)
                {
                    file_path = Path.GetFullPath("\\\\?\\" + file_path);
                    lock_path = Path.GetFullPath("\\\\?\\" + lock_path);
                    metadata_path = Path.GetFullPath("\\\\?\\" + metadata_path);
                }
            }

            FileHelper.MakeDir(Path.GetDirectoryName(file_path));
            FileHelper.MakeDir(Path.GetDirectoryName(metadata_path));

            return new LocalDownloadFilePaths() { file_path = file_path, lock_path = lock_path, metadata_path = metadata_path };
        }

        /// <summary>
        /// Read metadata about a file in the local directory related to a download process.
        /// </summary>
        /// <param name="local_dir"></param>
        /// <param name="filename"></param>
        /// <returns></returns>
        public static LocalDownloadFileMetadata read_download_metadata(string local_dir, string filename)
        {
            var paths = get_local_download_paths(local_dir, filename);

            LocalDownloadFileMetadata metadata = null;

            if (File.Exists(paths.metadata_path))
            {
                try
                {
                    var lines = File.ReadAllLines(paths.metadata_path);

                    var commit_hash = lines[0].Trim();
                    var etag = lines[1].Trim();
                    var timestamp = float.Parse(lines[2].Trim());

                    metadata = new LocalDownloadFileMetadata()
                    {
                        filename = filename,
                        commit_hash = commit_hash,
                        etag = etag,
                        timestamp = timestamp
                    };
                }
                catch (Exception ex)
                {
                    // remove the metadata file if it is corrupted / not the right format
                    LogHelper.LogWarnning($"Invalid metadata file {paths.metadata_path}: {ex.Message}. Removing it from disk and continue.");

                    try
                    {
                        File.Delete(paths.metadata_path);
                    }
                    catch (IOException ioe)
                    {
                        LogHelper.LogWarnning($"Could not remove corrupted metadata file {paths.metadata_path}: {ioe.Message}");
                    }

                    return null;
                }

                try
                {
                    // check if the file exists and hasn't been modified since the metadata was saved
                    var stat = new FileInfo(paths.file_path);

                    if (stat.LastAccessTimeUtc.GetTotalSeconds() - 1 <= metadata.timestamp)
                    {
                        // allow 1s difference as stat.st_mtime might not be precise
                        return metadata;
                    }

                    LogHelper.LogInfo($"Ignored metadata for '{filename}' (outdated). Will re-compute hash.");
                }
                catch (FileNotFoundException fnio)
                {
                    // file does not exist => metadata is outdated
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Return the path to the `.cache/huggingface` directory in a local directory.
        /// </summary>
        /// <param name="local_dir"></param>
        /// <returns></returns>
        private static string _huggingface_dir(string local_dir)
        {
            // Wrap in lru_cache to avoid overwriting the .gitignore file if called multiple times
            var path = Path.Combine(local_dir, ".cache", "huggingface");

            // Without long path support enabled, Windows caps directory paths at 247 characters
            // (MAX_PATH minus room for an 8.3 file name), so creating the `.cache/huggingface` directory
            // (and the bookkeeping files below) fails for a deep `local_dir`.
            // Use the extended-length "\\?\" prefix for the filesystem operations, matching what
            // `get_local_download_paths`/`get_local_upload_paths` already do for the download/upload paths.
            // The un-prefixed `path` is still returned so callers keep re-deriving/prefixing as before.

            var target = path;

            if (FileHelper.IsNT())
            {
                var abs_path = Path.GetFullPath(path);

                if (abs_path.Length > 247 && !abs_path.StartsWith("\\\\?\\"))
                {
                    target = Path.GetFullPath("\\\\?\\" + abs_path);
                }
            }

            var parent = new DirectoryInfo(target).Parent;

            if (!parent.Exists)
            {
                parent.Create();
            }

            if (!Directory.Exists(target))
            {
                Directory.CreateDirectory(target);
            }

            // Create a CACHEDIR.TAG so backup tools can skip this directory.
            _create_cachedir_tag(target);

            // Create a .gitignore file in the .cache/huggingface directory if it doesn't exist
            // Should be thread-safe enough like this.

            var gitignore = Path.Combine(target, ".gitignore");
            var gitignore_lock = Path.Combine(target, ".gitignore.lock");

            if (!File.Exists(gitignore))
            {
                try
                {
                    File.WriteAllText(gitignore, "*");
                }
                catch (Exception ex)
                {
                }

                try
                {
                    File.Delete(gitignore_lock);
                }
                catch (Exception ex)
                {
                }
            }

            return path;
        }

        /// <summary>
        /// Create a CACHEDIR.TAG file in ``cache_dir`` if one does not already exist.
        /// 
        /// The tag follows the `Cache Directory Tagging Standard <http://www.brynosaurus.com/cachedir/>`_
        /// so that backup tools can recognize and skip cache directories.
        /// </summary>
        /// <param name="cache_dir"></param>
        internal static void _create_cachedir_tag(string cache_dir)
        {
            var tag_path = Path.Combine(cache_dir, "CACHEDIR.TAG");

            if (!Directory.Exists(tag_path))
            {
                try
                {
                    File.WriteAllText(tag_path, CACHEDIR_TAG_CONTENT);
                }
                catch (Exception ex)
                {

                }
            }
        }

        /// <summary>
        /// Validate that a repo filename is safe to use as a path relative to a local directory.
        /// </summary>
        /// <param name="filename"></param>
        internal static void _validate_relative_filename(string filename)
        {
            // Reject parent-directory traversal ('..' as any segment, whether '/'- or '\'-separated).
            if (filename.Replace("\\", "/").Split('/').Contains(".."))
            {
                throw new ValueError($"Invalid filename '{filename}': cannot contain a '..' path segment. Please ask the repository owner to rename this file.");
            }

            // Reject anchored paths (absolute / drive / root-relative / UNC) under either OS's rules.
            // Check each POSIX segment under Windows rules because os.path.join handles them separately.

            //var pure_paths = [PurePosixPath(filename), *(PureWindowsPath(part) for part in filename.split("/"))] ////to do
        }

        /// <summary>
        /// Write metadata about a file in the local directory related to a download process.
        /// </summary>
        /// <param name="local_dir"></param>
        /// <param name="filename"></param>
        /// <param name="commit_hash"></param>
        /// <param name="etag"></param>
        public static void write_download_metadata(string local_dir, string filename, string commit_hash, string etag)
        {
            var paths = get_local_download_paths(local_dir, filename);

            File.WriteAllText(paths.metadata_path, $"{commit_hash}\n{etag}\n{DateTime.Now.GetTotalSeconds()}\n");
        }
    }
}
