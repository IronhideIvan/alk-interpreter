using System;
using System.Collections.Generic;
using ALKScript.Interpreter.Common.Ast;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>
  /// The "decls-before-statements" precondition for "Phase B" structural
  /// Capture (docs/ASYNC_AWAIT_DESIGN.md Addendum 3): every module's
  /// top-level <see cref="StatementDecl"/>-wrapped statements must come after
  /// all of its other (class/interface/enum/function/variable/import/export)
  /// declarations. <see cref="CursorProgramEvaluator.RestoreStructural"/>'s
  /// "run declarations, then graft" pass relies on
  /// <see cref="GetDeclarationPrefix"/> to re-create the module's
  /// <see cref="CapturedEnvironment.ModuleRef"/>-referenced environment (with
  /// its <c>ClassValue</c>/<c>FunctionValue</c>/etc bindings) before the
  /// captured trail is grafted on top.
  /// </summary>
  internal static class ModuleDeclarationPrefix
  {
    /// <summary>
    /// Returns the leading run of <paramref name="declarations"/> that are
    /// not <see cref="StatementDecl"/> — the declarations
    /// <see cref="CursorProgramEvaluator.RestoreStructural"/> runs to
    /// populate a module's environment before grafting the captured trail.
    /// </summary>
    public static IReadOnlyList<Stmt> GetDeclarationPrefix(IReadOnlyList<Stmt> declarations)
    {
      int count = 0;

      while (count < declarations.Count && !(declarations[count] is StatementDecl))
      {
        count++;
      }

      var prefix = new Stmt[count];
      for (int i = 0; i < count; i++)
      {
        prefix[i] = declarations[i];
      }

      return prefix;
    }

    /// <summary>
    /// Validates that <paramref name="declarations"/> satisfies the
    /// "decls-before-statements" precondition — every <see cref="StatementDecl"/>
    /// comes after all other declarations. Throws
    /// <see cref="NotSupportedException"/> (naming <paramref name="moduleKey"/>)
    /// if violated.
    /// </summary>
    public static void Validate(IReadOnlyList<Stmt> declarations, string moduleKey)
    {
      bool seenStatement = false;

      foreach (var declaration in declarations)
      {
        switch (declaration)
        {
          case StatementDecl:
            seenStatement = true;
            break;

          // Top-level `var` declarations are restored from the captured
          // environment regardless of position, so they may freely
          // interleave with top-level statements.
          case VariableDecl:
            break;

          default:
            if (seenStatement)
            {
              throw new NotSupportedException(
                $"Module '{moduleKey}' has a class/interface/enum/function/import/export declaration after a " +
                "top-level statement — structural Capture/Restore requires every such declaration to precede " +
                "all of a module's top-level statements (docs/ASYNC_AWAIT_DESIGN.md Addendum 3, " +
                "\"decls-before-statements\").");
            }
            break;
        }
      }
    }
  }
}
