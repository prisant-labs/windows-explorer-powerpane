using System;
using System.IO;

namespace PowerPane.Tests
{
    /// <summary>Shared temp-directory handling for tests that must never touch the real
    /// %APPDATA%\PowerPane. Each caller gets its own folder under the system temp path and is
    /// responsible for disposing it, which deletes that folder and nothing else.</summary>
    internal sealed class TempDir : IDisposable
    {
        public string Path { get; private set; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PowerPane.Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try { if (Directory.Exists(Path)) Directory.Delete(Path, true); } catch { /* best effort cleanup */ }
        }
    }
}
