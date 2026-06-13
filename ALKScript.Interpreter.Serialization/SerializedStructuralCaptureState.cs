using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Common.Token;
using ALKScript.Interpreter.Evaluator;
using ALKScript.Interpreter.Evaluator.Cursor;

namespace ALKScript.Interpreter.Serialization
{
  /// <summary>JSON-friendly representation of an <see cref="AstReference"/>.</summary>
  public sealed class SerializedAstReference
  {
    public string ModuleKey { get; set; } = "";

    public string Path { get; set; } = "";

    public static SerializedAstReference From(AstReference reference) => new SerializedAstReference
    {
      ModuleKey = reference.ModuleKey,
      Path = reference.Path,
    };

    public AstReference ToAstReference() => new AstReference(ModuleKey, Path);
  }

  /// <summary>JSON-friendly representation of an <see cref="ALKScriptToken"/>.</summary>
  public sealed class SerializedToken
  {
    public string Type { get; set; } = "";

    public string Lexeme { get; set; } = "";

    public int Line { get; set; }

    public int Column { get; set; }

    public static SerializedToken From(ALKScriptToken token) => new SerializedToken
    {
      Type = token.Type.ToString(),
      Lexeme = token.Lexeme,
      Line = token.Line,
      Column = token.Column,
    };

    public ALKScriptToken ToToken() => new ALKScriptToken(
      (ALKScriptTokenType)Enum.Parse(typeof(ALKScriptTokenType), Type),
      Lexeme,
      Line,
      Column);
  }

  /// <summary>JSON-friendly representation of a <see cref="CapturedHeapValue"/>.</summary>
  public sealed class SerializedHeapValue
  {
    public string Kind { get; set; } = "";

    public SerializedValue? Primitive { get; set; }

    public SerializedAstReference? AstRef { get; set; }

    public int? HeapRefId { get; set; }

    public SerializedAstReference? MethodRef { get; set; }

    public int? MethodInstanceId { get; set; }

    public int? PendingOpRefId { get; set; }

    public static SerializedHeapValue From(CapturedHeapValue value)
    {
      switch (value)
      {
        case CapturedHeapValue.Primitive primitive:
          return new SerializedHeapValue { Kind = "primitive", Primitive = SerializedValue.FromValue(primitive.Value) };

        case CapturedHeapValue.AstRef astRef:
          return new SerializedHeapValue { Kind = "astref", AstRef = SerializedAstReference.From(astRef.Reference) };

        case CapturedHeapValue.HeapRef heapRef:
          return new SerializedHeapValue { Kind = "heapref", HeapRefId = heapRef.Id };

        case CapturedHeapValue.Method method:
          return new SerializedHeapValue
          {
            Kind = "method",
            MethodRef = SerializedAstReference.From(method.Reference),
            MethodInstanceId = method.Instance.Id,
          };

        case CapturedHeapValue.NativeMethod nativeMethod:
          return new SerializedHeapValue
          {
            Kind = "nativemethod",
            MethodRef = SerializedAstReference.From(nativeMethod.Reference),
            MethodInstanceId = nativeMethod.Instance.Id,
          };

        case CapturedHeapValue.PendingOpRef pendingOpRef:
          return new SerializedHeapValue { Kind = "pendingopref", PendingOpRefId = pendingOpRef.Id };

        default:
          throw new NotSupportedException($"Cannot serialize captured heap value of type '{value.GetType().Name}'.");
      }
    }

    public CapturedHeapValue ToCapturedHeapValue()
    {
      switch (Kind)
      {
        case "primitive":
          return new CapturedHeapValue.Primitive((Primitive ?? throw new FormatException("Serialized 'primitive' heap value is missing Primitive.")).ToValue());

        case "astref":
          return new CapturedHeapValue.AstRef((AstRef ?? throw new FormatException("Serialized 'astref' heap value is missing AstRef.")).ToAstReference());

        case "heapref":
          return new CapturedHeapValue.HeapRef(HeapRefId ?? throw new FormatException("Serialized 'heapref' heap value is missing HeapRefId."));

        case "method":
          var methodRef = MethodRef ?? throw new FormatException("Serialized 'method' heap value is missing MethodRef.");
          var instanceId = MethodInstanceId ?? throw new FormatException("Serialized 'method' heap value is missing MethodInstanceId.");
          return new CapturedHeapValue.Method(methodRef.ToAstReference(), new CapturedHeapValue.HeapRef(instanceId));

        case "nativemethod":
          var nativeMethodRef = MethodRef ?? throw new FormatException("Serialized 'nativemethod' heap value is missing MethodRef.");
          var nativeInstanceId = MethodInstanceId ?? throw new FormatException("Serialized 'nativemethod' heap value is missing MethodInstanceId.");
          return new CapturedHeapValue.NativeMethod(nativeMethodRef.ToAstReference(), new CapturedHeapValue.HeapRef(nativeInstanceId));

        case "pendingopref":
          return new CapturedHeapValue.PendingOpRef(PendingOpRefId ?? throw new FormatException("Serialized 'pendingopref' heap value is missing PendingOpRefId."));

        default:
          throw new FormatException($"Unknown serialized heap value kind '{Kind}'.");
      }
    }

    public CapturedHeapValue.AstRef ToAstRefValue() => (CapturedHeapValue.AstRef)ToCapturedHeapValue();
  }

  /// <summary>JSON-friendly representation of a <see cref="CapturedHeapEntry"/>.</summary>
  public sealed class SerializedHeapEntry
  {
    public string Kind { get; set; } = "";

    public SerializedAstReference? ClassRef { get; set; }

    public Dictionary<string, SerializedHeapValue>? Fields { get; set; }

    public Dictionary<string, SerializedTypeNode>? TypeArguments { get; set; }

    public SerializedAstReference? SuperclassRef { get; set; }

    public int? InstanceId { get; set; }

    public static SerializedHeapEntry From(CapturedHeapEntry entry)
    {
      switch (entry)
      {
        case CapturedHeapEntry.Instance instance:
          return new SerializedHeapEntry
          {
            Kind = "instance",
            ClassRef = SerializedAstReference.From(instance.ClassRef),
            Fields = instance.Fields.ToDictionary(kvp => kvp.Key, kvp => SerializedHeapValue.From(kvp.Value)),
            TypeArguments = instance.TypeArguments.ToDictionary(kvp => kvp.Key, kvp => SerializedTypeNode.From(kvp.Value)),
          };

        case CapturedHeapEntry.Base @base:
          return new SerializedHeapEntry
          {
            Kind = "base",
            SuperclassRef = SerializedAstReference.From(@base.SuperclassRef),
            InstanceId = @base.InstanceId,
          };

        default:
          throw new NotSupportedException($"Cannot serialize captured heap entry of type '{entry.GetType().Name}'.");
      }
    }

    public CapturedHeapEntry ToCapturedHeapEntry()
    {
      switch (Kind)
      {
        case "instance":
          var classRef = ClassRef ?? throw new FormatException("Serialized 'instance' heap entry is missing ClassRef.");
          var fields = Fields ?? throw new FormatException("Serialized 'instance' heap entry is missing Fields.");
          var typeArguments = TypeArguments ?? new Dictionary<string, SerializedTypeNode>();
          return new CapturedHeapEntry.Instance(
            classRef.ToAstReference(),
            fields.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToCapturedHeapValue()),
            typeArguments.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToTypeNode()));

        case "base":
          var superclassRef = SuperclassRef ?? throw new FormatException("Serialized 'base' heap entry is missing SuperclassRef.");
          var instanceId = InstanceId ?? throw new FormatException("Serialized 'base' heap entry is missing InstanceId.");
          return new CapturedHeapEntry.Base(superclassRef.ToAstReference(), instanceId);

        default:
          throw new FormatException($"Unknown serialized heap entry kind '{Kind}'.");
      }
    }
  }

  /// <summary>JSON-friendly representation of a <see cref="CapturedClassStaticFields"/>.</summary>
  public sealed class SerializedClassStaticFields
  {
    public SerializedAstReference ClassRef { get; set; } = new();

    public Dictionary<string, SerializedHeapValue> Fields { get; set; } = new();

    public static SerializedClassStaticFields From(CapturedClassStaticFields staticFields) => new SerializedClassStaticFields
    {
      ClassRef = SerializedAstReference.From(staticFields.ClassRef),
      Fields = staticFields.Fields.ToDictionary(kvp => kvp.Key, kvp => SerializedHeapValue.From(kvp.Value)),
    };

    public CapturedClassStaticFields ToCapturedClassStaticFields() => new CapturedClassStaticFields(
      ClassRef.ToAstReference(),
      Fields.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToCapturedHeapValue()));
  }

  /// <summary>JSON-friendly representation of a <see cref="CapturedEnvironment"/>.</summary>
  public sealed class SerializedEnvironment
  {
    public int Id { get; set; }

    public int? EnclosingId { get; set; }

    public Dictionary<string, SerializedHeapValue> Values { get; set; } = new();

    public Dictionary<string, SerializedTypeNode?> Types { get; set; } = new();

    public List<string> Consts { get; set; } = new();

    public SerializedTypeNode? CurrentFunctionReturnType { get; set; }

    public Dictionary<string, SerializedTypeNode>? CurrentTypeArguments { get; set; }

    public bool IsInConstructor { get; set; }

    public SerializedAstReference? CurrentClass { get; set; }

    public string? ModuleRef { get; set; }

    public static SerializedEnvironment From(CapturedEnvironment environment) => new SerializedEnvironment
    {
      Id = environment.Id,
      EnclosingId = environment.EnclosingId,
      Values = environment.Values.ToDictionary(kvp => kvp.Key, kvp => SerializedHeapValue.From(kvp.Value)),
      Types = environment.Types.ToDictionary(kvp => kvp.Key, kvp => kvp.Value != null ? SerializedTypeNode.From(kvp.Value) : null),
      Consts = environment.Consts.ToList(),
      CurrentFunctionReturnType = environment.CurrentFunctionReturnType != null ? SerializedTypeNode.From(environment.CurrentFunctionReturnType) : null,
      CurrentTypeArguments = environment.CurrentTypeArguments?.ToDictionary(kvp => kvp.Key, kvp => SerializedTypeNode.From(kvp.Value)),
      IsInConstructor = environment.IsInConstructor,
      CurrentClass = environment.CurrentClass != null ? SerializedAstReference.From(environment.CurrentClass.Reference) : null,
      ModuleRef = environment.ModuleRef?.ModuleKey,
    };

    public CapturedEnvironment ToCapturedEnvironment() => new CapturedEnvironment
    {
      Id = Id,
      EnclosingId = EnclosingId,
      Values = Values.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToCapturedHeapValue()),
      Types = Types.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToTypeNode()),
      Consts = new HashSet<string>(Consts),
      CurrentFunctionReturnType = CurrentFunctionReturnType?.ToTypeNode(),
      CurrentTypeArguments = CurrentTypeArguments?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToTypeNode()),
      IsInConstructor = IsInConstructor,
      CurrentClass = CurrentClass != null ? new CapturedHeapValue.AstRef(CurrentClass.ToAstReference()) : null,
      ModuleRef = ModuleRef != null ? new ModuleEnvironmentRef(ModuleRef) : null,
    };
  }

  /// <summary>JSON-friendly representation of a <see cref="CapturedSignal"/>.</summary>
  public sealed class SerializedSignal
  {
    public SignalKind Kind { get; set; }

    public SerializedValue Value { get; set; } = new();

    public static SerializedSignal From(CapturedSignal signal) => new SerializedSignal
    {
      Kind = signal.Kind,
      Value = SerializedValue.FromValue(((CapturedHeapValue.Primitive)signal.Value).Value),
    };

    public CapturedSignal ToCapturedSignal() => new CapturedSignal
    {
      Kind = Kind,
      Value = new CapturedHeapValue.Primitive(Value.ToValue()),
    };
  }

  /// <summary>JSON-friendly representation of a <see cref="CapturedAwaitElement"/>.</summary>
  public sealed class SerializedAwaitElement
  {
    public string Kind { get; set; } = "";

    public SerializedHeapValue? Value { get; set; }

    public SerializedOperation? Operation { get; set; }

    public string? Message { get; set; }

    public SerializedTypeNode? ElementType { get; set; }

    public int? OperationRefId { get; set; }

    public static SerializedAwaitElement From(CapturedAwaitElement element)
    {
      switch (element)
      {
        case CapturedAwaitElement.OperationRef operationRef:
          return new SerializedAwaitElement
          {
            Kind = "operationref",
            OperationRefId = operationRef.Id,
            ElementType = operationRef.ElementType != null ? SerializedTypeNode.From(operationRef.ElementType) : null,
          };

        case CapturedAwaitElement.Resolved resolved:
          return new SerializedAwaitElement
          {
            Kind = "resolved",
            Value = SerializedHeapValue.From(resolved.Value),
            ElementType = resolved.ElementType != null ? SerializedTypeNode.From(resolved.ElementType) : null,
          };

        case CapturedAwaitElement.Reissue reissue:
          return new SerializedAwaitElement
          {
            Kind = "reissue",
            Operation = SerializedOperation.FromOperation(reissue.Operation),
            ElementType = reissue.ElementType != null ? SerializedTypeNode.From(reissue.ElementType) : null,
          };

        case CapturedAwaitElement.Fault fault:
          return new SerializedAwaitElement
          {
            Kind = "fault",
            Message = fault.Message,
            ElementType = fault.ElementType != null ? SerializedTypeNode.From(fault.ElementType) : null,
          };

        default:
          throw new NotSupportedException($"Cannot serialize captured await element of type '{element.GetType().Name}'.");
      }
    }

    public CapturedAwaitElement ToCapturedAwaitElement()
    {
      var elementType = ElementType?.ToTypeNode();

      switch (Kind)
      {
        case "operationref":
          var operationRefId = OperationRefId ?? throw new FormatException("Serialized 'operationref' await element is missing OperationRefId.");
          return new CapturedAwaitElement.OperationRef(operationRefId, elementType);

        case "resolved":
          var value = Value ?? throw new FormatException("Serialized 'resolved' await element is missing Value.");
          return new CapturedAwaitElement.Resolved(value.ToCapturedHeapValue(), elementType);

        case "reissue":
          var operation = Operation ?? throw new FormatException("Serialized 'reissue' await element is missing Operation.");
          return new CapturedAwaitElement.Reissue(operation.ToOperation(), elementType);

        case "fault":
          var message = Message ?? throw new FormatException("Serialized 'fault' await element is missing Message.");
          return new CapturedAwaitElement.Fault(message, elementType);

        default:
          throw new FormatException($"Unknown serialized await element kind '{Kind}'.");
      }
    }
  }

  /// <summary>JSON-friendly representation of a <see cref="CapturedPendingAwait"/>.</summary>
  public sealed class SerializedPendingAwait
  {
    public SerializedOperation? Operation { get; set; }

    public int? OperationRef { get; set; }

    public SerializedTypeNode? ElementType { get; set; }

    public SerializedToken Site { get; set; } = new();

    public List<SerializedAwaitElement>? CompositeElements { get; set; }

    public static SerializedPendingAwait From(CapturedPendingAwait pendingAwait) => new SerializedPendingAwait
    {
      Operation = pendingAwait.Operation != null ? SerializedOperation.FromOperation(pendingAwait.Operation) : null,
      OperationRef = pendingAwait.OperationRef,
      ElementType = pendingAwait.ElementType != null ? SerializedTypeNode.From(pendingAwait.ElementType) : null,
      Site = SerializedToken.From(pendingAwait.Site),
      CompositeElements = pendingAwait.CompositeElements?.Select(SerializedAwaitElement.From).ToList(),
    };

    public CapturedPendingAwait ToCapturedPendingAwait() => new CapturedPendingAwait
    {
      Operation = Operation?.ToOperation(),
      OperationRef = OperationRef,
      ElementType = ElementType?.ToTypeNode(),
      Site = Site.ToToken(),
      CompositeElements = CompositeElements?.Select(element => element.ToCapturedAwaitElement()).ToList(),
    };
  }

  /// <summary>JSON-friendly representation of a <see cref="CapturedPendingOperation"/>.</summary>
  public sealed class SerializedPendingOperation
  {
    public SerializedAwaitElement Element { get; set; } = new();

    public bool WasStarted { get; set; }

    public static SerializedPendingOperation From(CapturedPendingOperation operation) => new SerializedPendingOperation
    {
      Element = SerializedAwaitElement.From(operation.Element),
      WasStarted = operation.WasStarted,
    };

    public CapturedPendingOperation ToCapturedPendingOperation() => new CapturedPendingOperation(
      Element.ToCapturedAwaitElement(),
      WasStarted);
  }

  /// <summary>JSON-friendly representation of a <see cref="CursorStructuralCaptureState"/> — see <see cref="CursorStructuralStateSerializer"/>.</summary>
  public sealed class SerializedStructuralCaptureState
  {
    public string ModuleKey { get; set; } = "";

    public List<SerializedHeapEntry> Heap { get; set; } = new();

    public List<SerializedClassStaticFields> StaticFields { get; set; } = new();

    public List<SerializedPendingOperation> PendingOperations { get; set; } = new();

    public List<SerializedEnvironment> Environments { get; set; } = new();

    public List<SerializedTrailEntry> Trail { get; set; } = new();

    public int RootEnvironmentId { get; set; }

    public SerializedSignal? Signal { get; set; }

    public SerializedPendingAwait? PendingAwait { get; set; }
  }

  /// <summary>JSON-friendly representation of a <see cref="CapturedTrailEntry"/>.</summary>
  public sealed class SerializedTrailEntry
  {
    public int Index { get; set; }

    public int EnvironmentId { get; set; }

    public SerializedSignal? PendingSignal { get; set; }

    public static SerializedTrailEntry From(CapturedTrailEntry entry) => new SerializedTrailEntry
    {
      Index = entry.Index,
      EnvironmentId = entry.EnvironmentId,
      PendingSignal = entry.PendingSignal != null ? SerializedSignal.From(entry.PendingSignal) : null,
    };

    public CapturedTrailEntry ToCapturedTrailEntry() => new CapturedTrailEntry
    {
      Index = Index,
      EnvironmentId = EnvironmentId,
      PendingSignal = PendingSignal?.ToCapturedSignal(),
    };
  }

  /// <summary>
  /// The public entry point for capturing a suspended
  /// <see cref="CursorProgramEvaluator"/> run's "Phase B" structural snapshot
  /// (<see cref="CursorProgramEvaluator.CaptureStructural"/>,
  /// docs/ASYNC_AWAIT_DESIGN.md Addendum 3) to a JSON byte stream, and
  /// restoring one from it via <see cref="CursorProgramEvaluator.RestoreStructural"/>.
  /// </summary>
  public static class CursorStructuralStateSerializer
  {
    public static byte[] Capture(CursorProgramEvaluator evaluator)
    {
      var state = evaluator.CaptureStructural();

      var serialized = new SerializedStructuralCaptureState
      {
        ModuleKey = state.ModuleKey,
        Heap = state.Heap.Select(SerializedHeapEntry.From).ToList(),
        StaticFields = state.StaticFields.Select(SerializedClassStaticFields.From).ToList(),
        PendingOperations = state.PendingOperations.Select(SerializedPendingOperation.From).ToList(),
        Environments = state.Environments.Select(SerializedEnvironment.From).ToList(),
        Trail = state.Trail.Select(SerializedTrailEntry.From).ToList(),
        RootEnvironmentId = state.RootEnvironmentId,
        Signal = state.Signal != null ? SerializedSignal.From(state.Signal) : null,
        PendingAwait = state.PendingAwait != null ? SerializedPendingAwait.From(state.PendingAwait) : null,
      };

      return JsonSerializer.SerializeToUtf8Bytes(serialized);
    }

    public static CursorProgramEvaluator Restore(
      ModuleGraph graph,
      byte[] data,
      out ProgramRunResult result,
      ScriptNativeBindings? nativeBindings = null,
      ScriptNativeMethodBindings? nativeMethodBindings = null,
      IAsyncOperationBinder? operationBinder = null)
    {
      var serialized = JsonSerializer.Deserialize<SerializedStructuralCaptureState>(data)
        ?? throw new FormatException("Captured structural state JSON deserialized to null.");

      var state = new CursorStructuralCaptureState
      {
        ModuleKey = serialized.ModuleKey,
        Heap = serialized.Heap.Select(entry => entry.ToCapturedHeapEntry()).ToList(),
        StaticFields = serialized.StaticFields.Select(entry => entry.ToCapturedClassStaticFields()).ToList(),
        PendingOperations = serialized.PendingOperations.Select(entry => entry.ToCapturedPendingOperation()).ToList(),
        Environments = serialized.Environments.Select(entry => entry.ToCapturedEnvironment()).ToList(),
        Trail = serialized.Trail.Select(entry => entry.ToCapturedTrailEntry()).ToList(),
        RootEnvironmentId = serialized.RootEnvironmentId,
        Signal = serialized.Signal?.ToCapturedSignal(),
        PendingAwait = serialized.PendingAwait?.ToCapturedPendingAwait(),
      };

      return CursorProgramEvaluator.RestoreStructural(graph, state, out result, nativeBindings, nativeMethodBindings, operationBinder);
    }
  }
}
