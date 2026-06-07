using System.IO;

namespace ALKScript.Interpreter.Parser.Modules
{
  /// <summary>The default <see cref="IModuleFileReader"/>, backed by <see cref="System.IO"/>.</summary>
  public class FileSystemModuleFileReader : IModuleFileReader
  {
    public bool FileExists(string path)
    {
      return File.Exists(path);
    }

    public string ReadFile(string path)
    {
      return File.ReadAllText(path);
    }

    public string CombinePath(string directory, string relativePath)
    {
      return Path.GetFullPath(Path.Combine(directory, relativePath));
    }

    public string GetDirectoryName(string path)
    {
      return Path.GetDirectoryName(path) ?? string.Empty;
    }
  }
}
