namespace HuggingFaceHubSharp.utils
{
    public class WeakFileLock : IDisposable
    {
        private FileStream _lockStream;
        private WeakReference _weakRef;
        private bool _disposed;

        public bool IsLocked => _lockStream != null && !_disposed;

        public WeakFileLock(string lockFilePath)
        {
            _lockStream = new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            _weakRef = new WeakReference(this);
        }

        ~WeakFileLock()
        {
            ReleaseLock();
        }

        // 显式释放
        public void Dispose()
        {
            ReleaseLock();

            GC.SuppressFinalize(this);
        }

        private void ReleaseLock()
        {
            if (!_disposed && _lockStream != null)
            {
                _lockStream.Close();
                _lockStream = null;
                _disposed = true;
            }
        }       
    }
}
