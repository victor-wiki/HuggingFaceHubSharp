namespace HuggingFaceHubSharp.utils
{
    public class TemporaryDirectory:IDisposable
    {
        public string Path { get; private set; }

        public TemporaryDirectory(string dir = null, string prefix = null, string suffix = null)
        {
            Path = System.IO.Path.Combine(dir?? System.IO.Path.GetTempPath(), prefix?? "" + Guid.NewGuid().ToString("N") + suffix??"");

            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }                  
            }
            catch(Exception ex)
            {
            }
        }
    }
}
