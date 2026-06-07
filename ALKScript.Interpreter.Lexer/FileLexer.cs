using System.Collections.Generic;

namespace ALKScript.Interpreter.Lexer
{
  public class FileLexer
  {
    private static readonly Dictionary<string, TokenType> Keywords = new Dictionary<string, TokenType>
    {
      { "if", TokenType.If },
      { "else", TokenType.Else },
      { "while", TokenType.While },
      { "for", TokenType.For },
      { "function", TokenType.Function },
      { "return", TokenType.Return },
      { "let", TokenType.Let },
      { "true", TokenType.True },
      { "false", TokenType.False },
      { "null", TokenType.Null },
    };

    private string _source = string.Empty;
    private List<Token> _tokens = new List<Token>();
    private int _start;
    private int _current;
    private int _line;
    private int _column;
    private int _startColumn;

    public List<Token> Tokenize(string contents)
    {
      _source = contents;
      _tokens = new List<Token>();
      _start = 0;
      _current = 0;
      _line = 1;
      _column = 1;
      _startColumn = 1;

      while (!IsAtEnd())
      {
        _start = _current;
        _startColumn = _column;
        ScanToken();
      }

      _tokens.Add(new Token(TokenType.EndOfFile, string.Empty, _line, _column));
      return _tokens;
    }

    private void ScanToken()
    {
      char c = Advance();

      switch (c)
      {
        case '(': AddToken(TokenType.LeftParen); break;
        case ')': AddToken(TokenType.RightParen); break;
        case '{': AddToken(TokenType.LeftBrace); break;
        case '}': AddToken(TokenType.RightBrace); break;
        case '[': AddToken(TokenType.LeftBracket); break;
        case ']': AddToken(TokenType.RightBracket); break;
        case ',': AddToken(TokenType.Comma); break;
        case ';': AddToken(TokenType.Semicolon); break;
        case ':': AddToken(TokenType.Colon); break;
        case '.': AddToken(TokenType.Dot); break;
        case '+': AddToken(TokenType.Plus); break;
        case '-': AddToken(TokenType.Minus); break;
        case '*': AddToken(TokenType.Star); break;
        case '%': AddToken(TokenType.Percent); break;

        case '/':
          if (Match('/'))
          {
            while (Peek() != '\n' && !IsAtEnd())
            {
              Advance();
            }
          }
          else if (Match('*'))
          {
            ScanBlockComment();
          }
          else
          {
            AddToken(TokenType.Slash);
          }
          break;

        case '=':
          AddToken(Match('=') ? TokenType.EqualEqual : TokenType.Equal);
          break;
        case '!':
          AddToken(Match('=') ? TokenType.BangEqual : TokenType.Bang);
          break;
        case '<':
          AddToken(Match('=') ? TokenType.LessEqual : TokenType.Less);
          break;
        case '>':
          AddToken(Match('=') ? TokenType.GreaterEqual : TokenType.Greater);
          break;
        case '&':
          if (Match('&'))
          {
            AddToken(TokenType.AmpAmp);
          }
          break;
        case '|':
          if (Match('|'))
          {
            AddToken(TokenType.PipePipe);
          }
          break;

        case ' ':
        case '\r':
        case '\t':
          break;
        case '\n':
          _line++;
          _column = 1;
          break;

        case '"':
          ScanString();
          break;

        default:
          if (IsDigit(c))
          {
            ScanNumber();
          }
          else if (IsAlpha(c))
          {
            ScanIdentifier();
          }
          break;
      }
    }

    private void ScanBlockComment()
    {
      while (!IsAtEnd())
      {
        if (Peek() == '*' && PeekNext() == '/')
        {
          Advance();
          Advance();
          return;
        }

        if (Peek() == '\n')
        {
          _line++;
          _column = 0;
        }

        Advance();
      }
    }

    private void ScanString()
    {
      while (Peek() != '"' && !IsAtEnd())
      {
        if (Peek() == '\n')
        {
          _line++;
          _column = 0;
        }

        Advance();
      }

      if (IsAtEnd())
      {
        return;
      }

      Advance();

      string value = _source.Substring(_start + 1, _current - _start - 2);
      AddToken(TokenType.String, value);
    }

    private void ScanNumber()
    {
      while (IsDigit(Peek()))
      {
        Advance();
      }

      if (Peek() == '.' && IsDigit(PeekNext()))
      {
        Advance();

        while (IsDigit(Peek()))
        {
          Advance();
        }
      }

      AddToken(TokenType.Number);
    }

    private void ScanIdentifier()
    {
      while (IsAlphaNumeric(Peek()))
      {
        Advance();
      }

      string text = _source.Substring(_start, _current - _start);
      TokenType type = Keywords.TryGetValue(text, out var keywordType) ? keywordType : TokenType.Identifier;
      AddToken(type);
    }

    private bool Match(char expected)
    {
      if (IsAtEnd() || _source[_current] != expected)
      {
        return false;
      }

      _current++;
      _column++;
      return true;
    }

    private char Advance()
    {
      char c = _source[_current];
      _current++;
      _column++;
      return c;
    }

    private char Peek()
    {
      return IsAtEnd() ? '\0' : _source[_current];
    }

    private char PeekNext()
    {
      return _current + 1 >= _source.Length ? '\0' : _source[_current + 1];
    }

    private bool IsAtEnd()
    {
      return _current >= _source.Length;
    }

    private static bool IsDigit(char c)
    {
      return c >= '0' && c <= '9';
    }

    private static bool IsAlpha(char c)
    {
      return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
    }

    private static bool IsAlphaNumeric(char c)
    {
      return IsAlpha(c) || IsDigit(c);
    }

    private void AddToken(TokenType type)
    {
      string lexeme = _source.Substring(_start, _current - _start);
      _tokens.Add(new Token(type, lexeme, _line, _startColumn));
    }

    private void AddToken(TokenType type, string lexeme)
    {
      _tokens.Add(new Token(type, lexeme, _line, _startColumn));
    }
  }
}
