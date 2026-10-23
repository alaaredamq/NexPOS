namespace Application.Services.Licensing
{
    /// <summary>Where the activated license key text is kept on this PC.</summary>
    public interface ILicenseStorage
    {
        /// <summary>Returns the stored key text, or null when nothing is stored.</summary>
        string? Read();

        void Write(string text);
    }

    /// <summary>Stores the key in a small text file in the per-user app-data folder, next to the database.</summary>
    public sealed class FileLicenseStorage : ILicenseStorage
    {
        private readonly string _path;

        public FileLicenseStorage(string path) => _path = path;

        public string? Read() => File.Exists(_path) ? File.ReadAllText(_path) : null;

        public void Write(string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);

            // Write beside the target first so a crash can never leave a half-written license.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, text);
            File.Move(temp, _path, overwrite: true);
        }
    }
}
