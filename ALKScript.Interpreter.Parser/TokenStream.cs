using System.Collections.Generic;
using System.Linq;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Parser
{
  /// <summary>
  /// Provides cursor-based navigation over a fixed token sequence, used by
  /// <see cref="ALKScriptParser"/> to implement recursive-descent and
  /// speculative (checkpoint/restore) parsing.
  /// </summary>
  internal class TokenStream
  {
    private readonly List<ALKScriptToken> _tokens;
    private int _current;

    public TokenStream(IEnumerable<ALKScriptToken> tokens)
    {
      _tokens = tokens.ToList();
    }

    /// <summary>
    /// The index of the token that would be returned by <see cref="Peek"/>.
    /// Can be saved and restored to implement speculative parsing.
    /// </summary>
    public int Position
    {
      get => _current;
      set => _current = value;
    }

    public bool Match(params ALKScriptTokenType[] types)
    {
      foreach (ALKScriptTokenType type in types)
      {
        if (Check(type))
        {
          Advance();
          return true;
        }
      }

      return false;
    }

    public bool Check(ALKScriptTokenType type)
    {
      if (IsAtEnd())
      {
        return false;
      }

      return Peek().Type == type;
    }

    public bool CheckNext(ALKScriptTokenType type)
    {
      return CheckAhead(1, type);
    }

    /// <summary>
    /// True when the token <paramref name="offset"/> positions ahead of the
    /// cursor (0 = current) has the given type. Lets callers look past a
    /// variable run of optional modifier tokens without consuming them.
    /// </summary>
    public bool CheckAhead(int offset, ALKScriptTokenType type)
    {
      int index = _current + offset;

      if (index >= _tokens.Count)
      {
        return false;
      }

      return _tokens[index].Type == type;
    }

    public ALKScriptToken Advance()
    {
      if (!IsAtEnd())
      {
        _current++;
      }

      return Previous();
    }

    public bool IsAtEnd()
    {
      return Peek().Type == ALKScriptTokenType.EndOfFile;
    }

    public ALKScriptToken Peek()
    {
      return _tokens[_current];
    }

    public ALKScriptToken Previous()
    {
      return _tokens[_current - 1];
    }

    public ALKScriptToken Consume(ALKScriptTokenType type, string errorMessage)
    {
      if (Check(type))
      {
        return Advance();
      }

      throw new ParseException(Peek(), errorMessage);
    }
  }
}
