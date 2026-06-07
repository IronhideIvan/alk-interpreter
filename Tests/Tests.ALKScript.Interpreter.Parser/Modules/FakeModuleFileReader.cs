using System.Collections.Generic;
using ALKScript.Interpreter.Parser.Modules;

namespace Tests.ALKScript.Interpreter.Parser.Modules;

/// <summary>
/// An in-memory <see cref="IModuleFileReader"/> backed by a dictionary of
/// virtual file paths to source text, so <see cref="ProgramLoader"/> tests
/// don't need to touch the real filesystem. Paths use '/' as the separator
/// and are combined/normalized purely as strings (no OS-specific behavior).
/// </summary>
public class FakeModuleFileReader : IModuleFileReader
{
  private readonly IReadOnlyDictionary<string, string> _files;

  public FakeModuleFileReader(IReadOnlyDictionary<string, string> files)
  {
    _files = files;
  }

  public bool FileExists(string path)
  {
    return _files.ContainsKey(path);
  }

  public string ReadFile(string path)
  {
    return _files[path];
  }

  public string CombinePath(string directory, string relativePath)
  {
    var segments = new List<string>();

    foreach (string part in directory.Split('/'))
    {
      if (!string.IsNullOrEmpty(part))
      {
        segments.Add(part);
      }
    }

    foreach (string part in relativePath.Split('/'))
    {
      if (part.Length == 0 || part == ".")
      {
        continue;
      }

      if (part == "..")
      {
        if (segments.Count > 0)
        {
          segments.RemoveAt(segments.Count - 1);
        }
      }
      else
      {
        segments.Add(part);
      }
    }

    return string.Join("/", segments);
  }

  public string GetDirectoryName(string path)
  {
    int separatorIndex = path.LastIndexOf('/');
    return separatorIndex >= 0 ? path.Substring(0, separatorIndex) : string.Empty;
  }
}
