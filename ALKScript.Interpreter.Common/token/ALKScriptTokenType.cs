namespace ALKScript.Interpreter.Common.Token
{
  public enum ALKScriptTokenType
  {
    // Literals
    Identifier,
    Number,
    String,

    // Keywords - control flow / general
    If,
    Else,
    While,
    For,
    Foreach,
    In,
    Do,
    Break,
    Continue,
    Switch,
    Case,
    Default,
    Function,
    Native,
    Return,
    Var,
    True,
    False,
    Null,

    // Keywords - async/await
    Async,
    Await,

    // Keywords - exception handling
    Try,
    Catch,
    Finally,
    Throw,

    // Keywords - type names
    IntKeyword,
    LongKeyword,
    FloatKeyword,
    StringKeyword,
    BoolKeyword,
    VoidKeyword,

    // Keywords - classes
    Class,
    New,
    This,
    Base,
    Extends,
    Public,
    Protected,
    Private,
    Virtual,
    Abstract,
    Override,
    Sealed,
    Interface,
    Implements,
    Enum,

    // Keywords - type testing/casting
    Is,

    // Keywords - modules
    Import,
    Export,
    From,
    As,

    // Operators
    Plus,
    PlusPlus,
    PlusEqual,
    Minus,
    MinusMinus,
    MinusEqual,
    Star,
    StarEqual,
    Slash,
    SlashEqual,
    Percent,
    PercentEqual,
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

    // Operators - bitwise
    Amp,
    AmpEqual,
    Pipe,
    PipeEqual,
    Caret,
    CaretEqual,
    Tilde,
    LessLess,
    LessLessEqual,
    GreaterGreater,
    GreaterGreaterEqual,

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
    Question,
    QuestionQuestion,
    QuestionDot,

    // String interpolation
    InterpolatedStringStart,
    InterpolatedStringMid,
    InterpolatedStringEnd,

    EndOfFile
  }
}
