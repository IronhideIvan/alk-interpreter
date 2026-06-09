using System.Collections.Generic;
using ALKScript.Interpreter.Parser.Modules;

namespace Tests.ALKScript.Interpreter.Runtime.Support;

/// <summary>
/// An in-memory <see cref="IModuleFileReader"/> backed by a dictionary of
/// virtual file paths to source text, so tests can run file-based programs
/// without touching the real filesystem.
/// </summary>
internal class FakeModuleFileReader : IModuleFileReader
{
  private readonly IReadOnlyDictionary<string, string> _files;

  public FakeModuleFileReader(IReadOnlyDictionary<string, string> files)
  {
    _files = files;
  }

  public bool FileExists(string path) => _files.ContainsKey(path);

  public string ReadFile(string path) => _files[path];

  public string CombinePath(string directory, string relativePath)
  {
    var segments = new List<string>();

    foreach (string part in directory.Split('/'))
    {
      if (!string.IsNullOrEmpty(part)) segments.Add(part);
    }

    foreach (string part in relativePath.Split('/'))
    {
      if (part.Length == 0 || part == ".") continue;
      if (part == "..") { if (segments.Count > 0) segments.RemoveAt(segments.Count - 1); }
      else segments.Add(part);
    }

    return string.Join("/", segments);
  }

  public string GetDirectoryName(string path)
  {
    int i = path.LastIndexOf('/');
    return i >= 0 ? path.Substring(0, i) : string.Empty;
  }
}
