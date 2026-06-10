using System.Collections.Generic;
using ALKScript.Interpreter.Common;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Lexer
{
  public class ALKScriptLexer : IScriptLexer
  {
    private static readonly Dictionary<string, ALKScriptTokenType> Keywords = new Dictionary<string, ALKScriptTokenType>
    {
      { "if", ALKScriptTokenType.If },
      { "else", ALKScriptTokenType.Else },
      { "while", ALKScriptTokenType.While },
      { "for", ALKScriptTokenType.For },
      { "foreach", ALKScriptTokenType.Foreach },
      { "in", ALKScriptTokenType.In },
      { "do", ALKScriptTokenType.Do },
      { "break", ALKScriptTokenType.Break },
      { "continue", ALKScriptTokenType.Continue },
      { "switch", ALKScriptTokenType.Switch },
      { "case", ALKScriptTokenType.Case },
      { "default", ALKScriptTokenType.Default },
      { "function", ALKScriptTokenType.Function },
      { "native", ALKScriptTokenType.Native },
      { "return", ALKScriptTokenType.Return },
      { "var", ALKScriptTokenType.Var },
      { "true", ALKScriptTokenType.True },
      { "false", ALKScriptTokenType.False },
      { "null", ALKScriptTokenType.Null },

      { "async", ALKScriptTokenType.Async },
      { "await", ALKScriptTokenType.Await },

      { "try", ALKScriptTokenType.Try },
      { "catch", ALKScriptTokenType.Catch },
      { "finally", ALKScriptTokenType.Finally },
      { "throw", ALKScriptTokenType.Throw },

      { "int", ALKScriptTokenType.IntKeyword },
      { "long", ALKScriptTokenType.LongKeyword },
      { "float", ALKScriptTokenType.FloatKeyword },
      { "string", ALKScriptTokenType.StringKeyword },
      { "bool", ALKScriptTokenType.BoolKeyword },
      { "void", ALKScriptTokenType.VoidKeyword },

      { "class", ALKScriptTokenType.Class },
      { "new", ALKScriptTokenType.New },
      { "this", ALKScriptTokenType.This },
      { "base", ALKScriptTokenType.Base },
      { "extends", ALKScriptTokenType.Extends },
      { "public", ALKScriptTokenType.Public },
      { "protected", ALKScriptTokenType.Protected },
      { "private", ALKScriptTokenType.Private },
      { "virtual", ALKScriptTokenType.Virtual },
      { "abstract", ALKScriptTokenType.Abstract },
      { "override", ALKScriptTokenType.Override },
      { "sealed", ALKScriptTokenType.Sealed },
      { "interface", ALKScriptTokenType.Interface },
      { "implements", ALKScriptTokenType.Implements },
      { "enum", ALKScriptTokenType.Enum },
      { "static", ALKScriptTokenType.Static },

      { "is", ALKScriptTokenType.Is },

      { "import", ALKScriptTokenType.Import },
      { "export", ALKScriptTokenType.Export },
      { "from", ALKScriptTokenType.From },
      { "as", ALKScriptTokenType.As },
    };

    private string _source = string.Empty;
    private List<ALKScriptToken> _tokens = new List<ALKScriptToken>();
    private int _start;
    private int _current;
    private int _line;
    private int _column;
    private int _startColumn;
    private int _templateInterpolationDepth;

    public IEnumerable<ALKScriptToken> Tokenize(string contents)
    {
      _source = contents;
      _tokens = new List<ALKScriptToken>();
      _start = 0;
      _current = 0;
      _line = 1;
      _column = 1;
      _startColumn = 1;
      _templateInterpolationDepth = 0;

      while (!IsAtEnd())
      {
        _start = _current;
        _startColumn = _column;
        ScanToken();
      }

      _tokens.Add(new ALKScriptToken(ALKScriptTokenType.EndOfFile, string.Empty, _line, _column));
      return _tokens;
    }

    private void ScanToken()
    {
      char c = Advance();

      switch (c)
      {
        case '(': AddToken(ALKScriptTokenType.LeftParen); break;
        case ')': AddToken(ALKScriptTokenType.RightParen); break;
        case '{': AddToken(ALKScriptTokenType.LeftBrace); break;
        case '[': AddToken(ALKScriptTokenType.LeftBracket); break;
        case ']': AddToken(ALKScriptTokenType.RightBracket); break;
        case ',': AddToken(ALKScriptTokenType.Comma); break;
        case ';': AddToken(ALKScriptTokenType.Semicolon); break;
        case ':': AddToken(ALKScriptTokenType.Colon); break;
        case '.': AddToken(ALKScriptTokenType.Dot); break;
        case '?':
          if (Match('?')) AddToken(ALKScriptTokenType.QuestionQuestion);
          else if (Match('.')) AddToken(ALKScriptTokenType.QuestionDot);
          else AddToken(ALKScriptTokenType.Question);
          break;
        case '+':
          if (Match('+')) AddToken(ALKScriptTokenType.PlusPlus);
          else if (Match('=')) AddToken(ALKScriptTokenType.PlusEqual);
          else AddToken(ALKScriptTokenType.Plus);
          break;
        case '-':
          if (Match('-')) AddToken(ALKScriptTokenType.MinusMinus);
          else if (Match('=')) AddToken(ALKScriptTokenType.MinusEqual);
          else AddToken(ALKScriptTokenType.Minus);
          break;
        case '*': AddToken(Match('=') ? ALKScriptTokenType.StarEqual : ALKScriptTokenType.Star); break;
        case '%': AddToken(Match('=') ? ALKScriptTokenType.PercentEqual : ALKScriptTokenType.Percent); break;

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
          else if (Match('='))
          {
            AddToken(ALKScriptTokenType.SlashEqual);
          }
          else
          {
            AddToken(ALKScriptTokenType.Slash);
          }
          break;

        case '=':
          AddToken(Match('=') ? ALKScriptTokenType.EqualEqual : ALKScriptTokenType.Equal);
          break;
        case '!':
          AddToken(Match('=') ? ALKScriptTokenType.BangEqual : ALKScriptTokenType.Bang);
          break;
        case '<':
          if (Match('<'))
          {
            AddToken(Match('=') ? ALKScriptTokenType.LessLessEqual : ALKScriptTokenType.LessLess);
          }
          else
          {
            AddToken(Match('=') ? ALKScriptTokenType.LessEqual : ALKScriptTokenType.Less);
          }
          break;
        case '>':
          if (Match('>'))
          {
            AddToken(Match('=') ? ALKScriptTokenType.GreaterGreaterEqual : ALKScriptTokenType.GreaterGreater);
          }
          else
          {
            AddToken(Match('=') ? ALKScriptTokenType.GreaterEqual : ALKScriptTokenType.Greater);
          }
          break;
        case '&':
          if (Match('&'))
          {
            AddToken(ALKScriptTokenType.AmpAmp);
          }
          else if (Match('='))
          {
            AddToken(ALKScriptTokenType.AmpEqual);
          }
          else
          {
            AddToken(ALKScriptTokenType.Amp);
          }
          break;
        case '|':
          if (Match('|'))
          {
            AddToken(ALKScriptTokenType.PipePipe);
          }
          else if (Match('='))
          {
            AddToken(ALKScriptTokenType.PipeEqual);
          }
          else
          {
            AddToken(ALKScriptTokenType.Pipe);
          }
          break;
        case '^':
          AddToken(Match('=') ? ALKScriptTokenType.CaretEqual : ALKScriptTokenType.Caret);
          break;
        case '~':
          AddToken(ALKScriptTokenType.Tilde);
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

        case '`':
          ScanTemplateString();
          break;

        case '}':
          if (_templateInterpolationDepth > 0)
          {
            _templateInterpolationDepth--;
            ScanTemplateStringContinuation();
          }
          else
          {
            AddToken(ALKScriptTokenType.RightBrace);
          }
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
      var value = new System.Text.StringBuilder();

      while (Peek() != '"' && !IsAtEnd())
      {
        char c = Peek();

        if (c == '\n')
        {
          _line++;
          _column = 0;
          value.Append(Advance());
          continue;
        }

        if (c == '\\')
        {
          Advance();
          char escaped = Peek();

          switch (escaped)
          {
            case 'n': value.Append('\n'); break;
            case 't': value.Append('\t'); break;
            case 'r': value.Append('\r'); break;
            case '"': value.Append('"'); break;
            case '\\': value.Append('\\'); break;
            case '0': value.Append('\0'); break;
            default: value.Append(escaped); break;
          }

          if (!IsAtEnd())
          {
            Advance();
          }

          continue;
        }

        value.Append(Advance());
      }

      if (IsAtEnd())
      {
        return;
      }

      Advance();

      AddToken(ALKScriptTokenType.String, value.ToString());
    }

    private void ScanTemplateString()
    {
      ScanTemplateSegment(isFirst: true);
    }

    private void ScanTemplateStringContinuation()
    {
      ScanTemplateSegment(isFirst: false);
    }

    private void ScanTemplateSegment(bool isFirst)
    {
      var value = new System.Text.StringBuilder();

      while (true)
      {
        if (IsAtEnd())
        {
          AddToken(ALKScriptTokenType.InterpolatedStringEnd, value.ToString());
          return;
        }

        char c = Peek();

        if (c == '`')
        {
          Advance();
          AddToken(ALKScriptTokenType.InterpolatedStringEnd, value.ToString());
          return;
        }

        if (c == '$' && PeekNext() == '{')
        {
          Advance();
          Advance();
          AddToken(isFirst ? ALKScriptTokenType.InterpolatedStringStart : ALKScriptTokenType.InterpolatedStringMid, value.ToString());
          _templateInterpolationDepth++;
          return;
        }

        if (c == '\n')
        {
          _line++;
          _column = 0;
          value.Append(Advance());
          continue;
        }

        if (c == '\\')
        {
          Advance();
          char escaped = Peek();

          switch (escaped)
          {
            case 'n': value.Append('\n'); break;
            case 't': value.Append('\t'); break;
            case 'r': value.Append('\r'); break;
            case '`': value.Append('`'); break;
            case '$': value.Append('$'); break;
            case '\\': value.Append('\\'); break;
            case '0': value.Append('\0'); break;
            default: value.Append(escaped); break;
          }

          if (!IsAtEnd())
          {
            Advance();
          }

          continue;
        }

        value.Append(Advance());
      }
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

      if (Peek() == 'L' || Peek() == 'l')
      {
        Advance();
      }

      AddToken(ALKScriptTokenType.Number);
    }

    private void ScanIdentifier()
    {
      while (IsAlphaNumeric(Peek()))
      {
        Advance();
      }

      string text = _source.Substring(_start, _current - _start);
      ALKScriptTokenType type = Keywords.TryGetValue(text, out var keywordType) ? keywordType : ALKScriptTokenType.Identifier;
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

    private void AddToken(ALKScriptTokenType type)
    {
      string lexeme = _source.Substring(_start, _current - _start);
      _tokens.Add(new ALKScriptToken(type, lexeme, _line, _startColumn));
    }

    private void AddToken(ALKScriptTokenType type, string lexeme)
    {
      _tokens.Add(new ALKScriptToken(type, lexeme, _line, _startColumn));
    }
  }
}
