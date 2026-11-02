using SharpUtility.Helper;
using System.Security.Cryptography;
using System.Text;

namespace HuggingFaceHubSharp.Model
{
    public class LocalDownloadFilePaths
    {
        public string file_path { get; set; }
        public string lock_path { get; set; }
        public string metadata_path { get; set; }

        /// <summary>
        /// Return the path where a file will be temporarily downloaded before being moved to `file_path`.
        /// </summary>
        /// <param name="etag"></param>
        /// <returns></returns>
        public string incomplete_path(string etag)
        {
            var path = Path.Combine(Path.GetDirectoryName(this.metadata_path), $"{_short_hash(Path.GetFileName(this.metadata_path))}.{etag}.incomplete");
            var resolved_path = path;

            // Some Windows versions do not allow for paths longer than 255 characters.
            // In this case, we must specify it as an extended path by using the "\\?\" prefix.
            if (FileHelper.IsNT() && resolved_path.Length > 255 && resolved_path.StartsWith("\\\\?\\"))
            {
                path = Path.GetFullPath("\\\\?\\" + resolved_path);
            }

            return path;
        }

        private string _short_hash(string filename)
        {
            return Convert.ToBase64String(SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(filename))).Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }
    }
}
