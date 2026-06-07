namespace ALKScript.Interpreter.Common.Token
{
  public class ALKScriptToken
  {
    public ALKScriptTokenType Type { get; }
    public string Lexeme { get; }
    public int Line { get; }
    public int Column { get; }

    public ALKScriptToken(ALKScriptTokenType type, string lexeme, int line, int column)
    {
      Type = type;
      Lexeme = lexeme;
      Line = line;
      Column = column;
    }

    public override string ToString()
    {
      return $"{Type} '{Lexeme}' (line {Line}, col {Column})";
    }
  }
}
