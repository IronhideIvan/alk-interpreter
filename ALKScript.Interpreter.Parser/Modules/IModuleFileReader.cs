namespace ALKScript.Interpreter.Parser.Modules
{
  /// <summary>
  /// Abstracts the filesystem access <see cref="ProgramLoader"/> needs to
  /// resolve and read file-path module specifiers, so that module loading can
  /// be unit-tested against in-memory sources rather than the real filesystem.
  /// </summary>
  public interface IModuleFileReader
  {
    /// <summary>True when a file exists at the given fully-qualified path.</summary>
    bool FileExists(string path);

    /// <summary>Reads the full contents of the file at the given fully-qualified path.</summary>
    string ReadFile(string path);

    /// <summary>
    /// Combines a directory and a relative path into a single, normalized,
    /// fully-qualified path (resolving "." and ".." segments).
    /// </summary>
    string CombinePath(string directory, string relativePath);

    /// <summary>Returns the directory portion of a fully-qualified file path.</summary>
    string GetDirectoryName(string path);
  }
}
