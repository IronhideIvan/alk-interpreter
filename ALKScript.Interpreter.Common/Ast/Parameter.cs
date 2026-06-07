namespace ALKScript.Interpreter.Common.Ast
{
  /// <summary>
  /// A single function/method/constructor parameter: "type IDENTIFIER".
  /// </summary>
  public class Parameter
  {
    public TypeNode Type { get; }
    public string Name { get; }

    public Parameter(TypeNode type, string name)
    {
      Type = type;
      Name = name;
    }
  }
}
