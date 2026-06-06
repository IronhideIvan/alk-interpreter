using ALK.Interpreter.Lexer;

namespace ALK.Interpreter.Console;

public class Executor
{
  public void Run()
  {
    var lexer = new FileLexer();
    System.Console.WriteLine("Hello, World!");
  }
}