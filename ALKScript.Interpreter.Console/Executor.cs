using ALKScript.Interpreter.Lexer;

namespace ALKScript.Interpreter.Console;

public class Executor
{
  public void Run()
  {
    var lexer = new ALKScriptLexer();
    var tokens = lexer.Tokenize("let x = 1 + 2;");

    foreach (var token in tokens)
    {
      System.Console.WriteLine(token);
    }
  }
}