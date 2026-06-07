namespace ALKScript.Interpreter.Lexer
{
  public enum TokenType
  {
    // Literals
    Identifier,
    Number,
    String,

    // Keywords
    If,
    Else,
    While,
    For,
    Function,
    Return,
    Let,
    True,
    False,
    Null,

    // Operators
    Plus,
    Minus,
    Star,
    Slash,
    Percent,
    Equal,
    EqualEqual,
    Bang,
    BangEqual,
    Less,
    LessEqual,
    Greater,
    GreaterEqual,
    AmpAmp,
    PipePipe,

    // Punctuation
    LeftParen,
    RightParen,
    LeftBrace,
    RightBrace,
    LeftBracket,
    RightBracket,
    Comma,
    Semicolon,
    Colon,
    Dot,

    EndOfFile
  }
}
