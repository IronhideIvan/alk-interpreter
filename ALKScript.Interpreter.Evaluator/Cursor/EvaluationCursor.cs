using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ALKScript.Interpreter.Common.Ast;
using ALKScript.Interpreter.Common.Evaluation;
using ALKScript.Interpreter.Common.Evaluation.Scheduling;
using ALKScript.Interpreter.Common.Evaluation.Values;
using ALKScript.Interpreter.Common.Modules;
using ALKScript.Interpreter.Common.Token;

namespace ALKScript.Interpreter.Evaluator.Cursor
{
  /// <summary>The outcome of <see cref="EvaluationCursor.Run"/>/<see cref="EvaluationCursor.Resume"/>/<see cref="EvaluationCursor.ResumeFaulted"/>.</summary>
  internal enum RunResult
  {
    Completed,
    Awaiting,
  }

  /// <summary>
  /// One level of the "resume trail" recorded when a top-level run suspends:
  /// which child (by statement/iteration/case/region index) a resumable
  /// construct was executing, and the <see cref="ScriptEnvironment"/> it was
  /// using for that child. <see cref="EvaluationCursor.Resume"/> walks the
  /// trail root-to-leaf (it is recorded leaf-to-root) to fast-forward back to
  /// the exact suspended statement without re-evaluating anything already
  /// evaluated.
  /// </summary>
  internal readonly struct TrailEntry
  {
    public int Index { get; }
    public ScriptEnvironment Environment { get; }

    /// <summary>
    /// For a <c>try</c>/<c>finally</c> region only: the <see cref="Signal"/>
    /// (return/throw/etc.) that was pending *before* the <c>finally</c> block
    /// started running — restored after <c>finally</c> completes unless
    /// <c>finally</c> itself raised a new one. Persisted here because it
    /// would otherwise be a lost local variable across suspension.
    /// </summary>
    public Signal? PendingSignal { get; }

    public TrailEntry(int index, ScriptEnvironment environment, Signal? pendingSignal = null)
    {
      Index = index;
      Environment = environment;
      PendingSignal = pendingSignal;
    }
  }

  /// <summary>
  /// A synchronous, resumable replacement for the <c>async Task</c>-based
  /// evaluator spine. Walks the AST node-by-node via <see cref="StepResult"/>;
  /// when an <c>await</c> on an unresolved <c>thunk</c>/<c>thunk&lt;T&gt;</c>
  /// is hit, the walk returns <see cref="RunResult.Awaiting"/> instead of
  /// blocking, and the host resumes it later via <see cref="Resume"/>.
  ///
  /// <see cref="Signal"/> is the orthogonal non-local-exit mechanism
  /// (break/continue/return/thrown/cancelled), unchanged from
  /// <see cref="IEvaluationContext"/> — checked after each sub-step exactly as
  /// it was checked after each <c>await</c> in the old evaluator.
  ///
  /// Suspension/resumption (Step 6 of the cursor-rewrite plan, docs:
  /// validated-nibbling-narwhal) is implemented via a "resume trail": when a
  /// statement's expression returns <see cref="StepResult.Awaiting"/>, every
  /// enclosing resumable construct (block/loop/if/switch/try) records, in
  /// <see cref="_trail"/>, which child it was executing and with which
  /// environment. <see cref="Resume"/> walks that trail back down to the
  /// exact suspended statement — re-entering only the chosen branches/
  /// iterations/regions, without re-evaluating already-evaluated expressions
  /// — substitutes the resumed value for the <c>await</c> that suspended, and
  /// continues normal execution from there.
  ///
  /// A called function/constructor body may itself suspend — its
  /// <see cref="ExecuteBlock"/> call participates in the same flat trail as
  /// any other block, with no dedicated trail entry of its own. Field/
  /// static-field initializers and native array-method callbacks remain
  /// restricted (see <see cref="CursorCallInvoker"/>'s <c>DisallowSuspension</c>).
  /// </summary>
  internal sealed class EvaluationCursor
  {
    private readonly CursorExpressionEvaluator _expressionEvaluator;
    private readonly CursorStatementExecutor _statementExecutor;
    private readonly CursorCallInvoker _callInvoker;

    private IReadOnlyList<Stmt>? _rootStatements;
    private ScriptEnvironment? _rootEnvironment;

    /// <summary>The suspended run's resume trail, leaf-to-root. Empty when not suspended.</summary>
    private List<TrailEntry> _trail = new();

    /// <summary>Entries recorded by a (possibly new) suspension during the current <see cref="Run"/>.</summary>
    private List<TrailEntry> _newTrail = new();

    /// <summary>
    /// Index into <see cref="_trail"/> of the next entry to consume,
    /// decremented as each enclosing construct fast-forwards into its
    /// resumed child. <c>-1</c> means "not resuming" — execute normally.
    /// </summary>
    private int _resumeCursor = -1;

    /// <summary>The value to substitute for the <c>await</c> that suspended, set by <see cref="Resume"/>.</summary>
    private ALKScriptValue? _resumeValue;

    /// <summary>The composite elements of a resumed <c>await [a, b, c]</c>, set by <see cref="Resume"/> when <see cref="AwaitHandle.CompositeElements"/> was set.</summary>
    private IReadOnlyList<AwaitElement>? _resumeComposite;

    /// <summary>
    /// The record-and-replay log (docs/ASYNC_AWAIT_DESIGN.md decision #17),
    /// carried over unchanged from the old evaluator's <c>IEvaluationContext.Log</c>/
    /// <c>TryReplayNext</c>/<c>RecordEntry</c>. During a replay run this starts
    /// pre-populated with <paramref name="replayLog"/>; entries beyond
    /// <see cref="_replayLength"/> are newly recorded during this run.
    /// </summary>
    private readonly List<OperationLogEntry> _log = new();
    private int _replayIndex;
    private readonly int _replayLength;

    public EvaluationCursor(IFunctionValueFactory functionValueFactory, IReadOnlyList<OperationLogEntry>? replayLog = null)
    {
      FunctionValueFactory = functionValueFactory;
      _expressionEvaluator = new CursorExpressionEvaluator(this, functionValueFactory);
      _statementExecutor = new CursorStatementExecutor(this);
      _callInvoker = new CursorCallInvoker(this);

      if (replayLog != null)
      {
        _log.AddRange(replayLog);
      }

      _replayLength = _log.Count;
    }

    /// <summary>The ordered log of every <c>async native</c> operation outcome recorded during this run. See <see cref="ProgramEvaluator.Log"/>.</summary>
    public IReadOnlyList<OperationLogEntry> Log => _log;

    /// <summary>Used by <see cref="CursorStatementExecutor"/> to create callable values for top-level/nested <see cref="FunctionDecl"/>/<see cref="ClassDecl"/> declarations.</summary>
    public IFunctionValueFactory FunctionValueFactory { get; }

    /// <summary>Consumes and returns the next replay-log entry, or <c>null</c> once the replay log is exhausted (live execution).</summary>
    public OperationLogEntry? TryReplayNext() => _replayIndex < _replayLength ? _log[_replayIndex++] : null;

    /// <summary>Appends a newly-settled operation's outcome to the log for future replay.</summary>
    public void RecordEntry(OperationLogEntry entry) => _log.Add(entry);

    /// <summary>
    /// Snapshots the record-and-replay log accumulated so far, for use by
    /// <see cref="CursorProgramEvaluator.Capture"/> (docs/ASYNC_AWAIT_DESIGN.md
    /// Addendum 3, "Phase A" Capture/Restore). Only valid while suspended
    /// (<see cref="PendingAwait"/> is non-null) — the resulting log, fed back
    /// into a fresh <see cref="EvaluationCursor"/>'s <c>replayLog</c>
    /// constructor parameter and re-run from the start, replays back to this
    /// exact suspension point.
    /// </summary>
    public IReadOnlyList<OperationLogEntry> Capture()
    {
      if (PendingAwait == null)
      {
        throw new InvalidOperationException("EvaluationCursor.Capture called while not awaiting.");
      }

      return new List<OperationLogEntry>(_log);
    }

    /// <summary>
    /// Captures a "Phase B" structural snapshot of this suspended run
    /// (docs/ASYNC_AWAIT_DESIGN.md Addendum 3) for later
    /// <see cref="RestoreSuspendedState"/>. Only valid while
    /// <see cref="PendingAwait"/> is non-null, on a single-element
    /// suspension backed by a <see cref="PendingOperation"/>
    /// (<see cref="AwaitHandle.Operation"/> non-null, <see cref="AwaitHandle.CompositeElements"/>
    /// null), and only with int/float/string/bool/null/array bindings
    /// reachable from the suspended trail (<see cref="CapturedHeapValue.FromPrimitive"/>).
    ///
    /// <paramref name="moduleKeysByEnvironment"/> identifies the pre-existing
    /// module-scope/global <see cref="ScriptEnvironment"/>s
    /// (<see cref="CapturedEnvironment.ModuleRef"/>) so
    /// <see cref="CursorProgramEvaluator.RestoreStructural"/>'s "run
    /// declarations, then graft" pass can map them back onto the
    /// environments it re-creates, instead of allocating fresh ones.
    /// </summary>
    public CursorStructuralCaptureState CaptureStructural(ModuleGraph graph, IReadOnlyDictionary<ScriptEnvironment, string> moduleKeysByEnvironment)
    {
      var pending = PendingAwait ?? throw new InvalidOperationException("EvaluationCursor.CaptureStructural called while not awaiting.");
      var addressTable = AstResolver.BuildAddressTable(graph);

      for (int i = 0; i < graph.GlobalPreludes.Count; i++)
      {
        ModuleDeclarationPrefix.Validate(graph.GlobalPreludes[i].Declarations, AstReference.ForPrelude(i));
      }

      foreach (var module in graph.Modules.Values)
      {
        ModuleDeclarationPrefix.Validate(module.Program.Declarations, AstReference.ForModule(module.Identifier));
      }

      var staticFieldsCaptured = new HashSet<ClassValue>(ReferenceEqualityComparer<ClassValue>.Instance);
      var staticFields = new List<CapturedClassStaticFields>();

      bool TryCaptureAstRef(ALKScriptValue value, out CapturedHeapValue.AstRef? astRef)
      {
        Decl? declaration = value switch
        {
          ClassValue classValue => classValue.Declaration,
          InterfaceValue interfaceValue => interfaceValue.Declaration,
          EnumTypeValue enumTypeValue => enumTypeValue.Declaration,
          FunctionValue { DeclaringClass: null, BoundInstance: null } functionValue => functionValue.Declaration,
          _ => null,
        };

        if (declaration != null && addressTable.TryGetValue(declaration, out var reference))
        {
          astRef = new CapturedHeapValue.AstRef(reference);

          if (value is ClassValue classValue)
          {
            CaptureClassStaticFields(classValue, reference);
          }

          return true;
        }

        astRef = null;
        return false;
      }

      void CaptureClassStaticFields(ClassValue classValue, AstReference classRef)
      {
        if (classValue.StaticFields.Count > 0 && staticFieldsCaptured.Add(classValue))
        {
          var fields = new Dictionary<string, CapturedHeapValue>();
          foreach (var field in classValue.StaticFields)
          {
            fields[field.Key] = CaptureValue(field.Value);
          }

          staticFields.Add(new CapturedClassStaticFields(classRef, fields));
        }

        if (classValue.Superclass != null && addressTable.TryGetValue(classValue.Superclass.Declaration, out var superclassRef))
        {
          CaptureClassStaticFields(classValue.Superclass, superclassRef);
        }
      }

      IReadOnlyList<AwaitElement>? compositeElementsToCapture = pending.CompositeElements;

      if (compositeElementsToCapture == null)
      {
        if (pending.Operation == null)
        {
          throw new NotSupportedException(
            "Capturing a suspension that is not backed by a 'thunk' native operation descriptor " +
            "(AwaitHandle.Operation) is not yet supported by structural Capture/Restore " +
            "(docs/ASYNC_AWAIT_DESIGN.md Addendum 3).");
        }

        foreach (var argument in pending.Operation.Arguments)
        {
          CapturedHeapValue.FromPrimitive(argument);
        }
      }

      var heap = new List<CapturedHeapEntry>();
      var heapIds = new Dictionary<ALKScriptValue, int>(ReferenceEqualityComparer<ALKScriptValue>.Instance);

      int GetHeapId(ALKScriptValue value)
      {
        if (heapIds.TryGetValue(value, out int existingHeapId))
        {
          return existingHeapId;
        }

        int heapId = heap.Count;
        heapIds[value] = heapId;
        heap.Add(null!); // reserve the slot so cyclic field references can resolve before this entry is filled in.

        CapturedHeapEntry entry;
        switch (value)
        {
          case InstanceValue instanceValue:
            if (!addressTable.TryGetValue(instanceValue.Class.Declaration, out var classRef))
            {
              throw new NotSupportedException(
                $"Cannot capture an instance of class '{instanceValue.Class.Declaration.Name.Lexeme}' — only instances of top-level " +
                "classes are supported by the structural Capture/Restore design's current milestone (docs/ASYNC_AWAIT_DESIGN.md Addendum 3, Step 9).");
            }

            CaptureClassStaticFields(instanceValue.Class, classRef);

            var fields = new Dictionary<string, CapturedHeapValue>();
            foreach (var field in instanceValue.Fields)
            {
              fields[field.Key] = CaptureValue(field.Value);
            }

            entry = new CapturedHeapEntry.Instance(classRef, fields, instanceValue.TypeArguments);
            break;

          case BaseValue baseValue:
            if (!addressTable.TryGetValue(baseValue.Superclass.Declaration, out var superclassRef))
            {
              throw new NotSupportedException(
                $"Cannot capture a 'base' reference to superclass '{baseValue.Superclass.Declaration.Name.Lexeme}' — only references to " +
                "top-level classes are supported by the structural Capture/Restore design's current milestone (docs/ASYNC_AWAIT_DESIGN.md Addendum 3, Step 9).");
            }

            entry = new CapturedHeapEntry.Base(superclassRef, GetHeapId(baseValue.Instance));
            break;

          default:
            throw new NotSupportedException(
              $"Cannot capture a value of runtime type '{value.GetType().Name}' (ALKScript type '{value.TypeName}') as a heap object — " +
              "only 'InstanceValue'/'BaseValue' are supported by the structural Capture/Restore design's current milestone " +
              "(docs/ASYNC_AWAIT_DESIGN.md Addendum 3, Step 9).");
        }

        heap[heapId] = entry;
        return heapId;
      }

      bool TryCaptureValue(ALKScriptValue value, out CapturedHeapValue? captured)
      {
        if (CapturedHeapValue.TryFromPrimitive(value, out var primitive))
        {
          captured = primitive;
          return true;
        }

        if (TryCaptureAstRef(value, out var astRef))
        {
          captured = astRef;
          return true;
        }

        if (value is InstanceValue or BaseValue)
        {
          captured = new CapturedHeapValue.HeapRef(GetHeapId(value));
          return true;
        }

        if (value is FunctionValue { DeclaringClass: not null, BoundInstance: not null } method
          && addressTable.TryGetValue(method.DeclaringClass.Declaration, out var classRef))
        {
          var methodRef = AstResolver.AddressOfMember(classRef, method.Declaration.Name.Lexeme);
          captured = new CapturedHeapValue.Method(methodRef, new CapturedHeapValue.HeapRef(GetHeapId(method.BoundInstance)));
          return true;
        }

        captured = null;
        return false;
      }

      CapturedHeapValue CaptureValue(ALKScriptValue value)
      {
        if (TryCaptureValue(value, out var captured))
        {
          return captured!;
        }

        throw new NotSupportedException(
          $"Cannot capture a value of runtime type '{value.GetType().Name}' (ALKScript type '{value.TypeName}') — " +
          "lambda values and native function values are not yet supported by the structural Capture/Restore design's " +
          "current milestone (docs/ASYNC_AWAIT_DESIGN.md Addendum 3, Step 13).");
      }

      var environments = new List<CapturedEnvironment>();
      var ids = new Dictionary<ScriptEnvironment, int>(ReferenceEqualityComparer<ScriptEnvironment>.Instance);

      int GetId(ScriptEnvironment env)
      {
        if (ids.TryGetValue(env, out int existingId))
        {
          return existingId;
        }

        int id = environments.Count;
        ids[env] = id;
        environments.Add(null!); // reserve the slot so nested EnclosingId lookups can resolve before this entry is filled in.

        var captured = new CapturedEnvironment
        {
          Id = id,
          EnclosingId = env.Enclosing != null ? GetId(env.Enclosing) : (int?)null,
          CurrentFunctionReturnType = env.OwnCurrentFunctionReturnType,
          CurrentTypeArguments = env.OwnCurrentTypeArguments,
          IsInConstructor = env.OwnIsInConstructor,
        };

        if (env.OwnCurrentClass != null)
        {
          if (!TryCaptureAstRef(env.OwnCurrentClass, out var currentClassRef))
          {
            throw new NotSupportedException(
              "Capturing an environment whose 'CurrentClass' is not a top-level class declaration is not yet supported " +
              "by structural Capture/Restore (docs/ASYNC_AWAIT_DESIGN.md Addendum 3, Step 8+).");
          }

          captured.CurrentClass = currentClassRef;
        }

        bool isModuleEnvironment = moduleKeysByEnvironment.TryGetValue(env, out string? moduleKey);

        foreach (var binding in env.OwnBindings)
        {
          if (TryCaptureValue(binding.Value, out var capturedValue))
          {
            captured.Values[binding.Key] = capturedValue!;
          }
          else if (!isModuleEnvironment)
          {
            throw new NotSupportedException(
              $"Cannot capture a value of runtime type '{binding.Value.GetType().Name}' (ALKScript type '{binding.Value.TypeName}') " +
              $"bound to '{binding.Key}' — only int/float/string/bool/null/array values, top-level class/interface/enum/" +
              "function declarations, class instances, and bound method values are supported by the structural " +
              "Capture/Restore design's current milestone (docs/ASYNC_AWAIT_DESIGN.md Addendum 3, Step 13).");
          }

          // Else: a non-primitive, non-AstRef-addressable binding (e.g. a
          // top-level ClassValue/FunctionValue) on a module/global environment —
          // CursorProgramEvaluator.RestoreStructural's declaration-prefix run
          // re-creates it, so it doesn't need to be captured here.
        }

        foreach (var type in env.OwnTypes)
        {
          captured.Types[type.Key] = type.Value;
        }

        foreach (var constName in env.OwnConsts)
        {
          captured.Consts.Add(constName);
        }

        if (isModuleEnvironment)
        {
          captured.ModuleRef = new ModuleEnvironmentRef(moduleKey!);
        }

        environments[id] = captured;
        return id;
      }

      int rootEnvironmentId = GetId(_rootEnvironment!);

      var trail = new List<CapturedTrailEntry>();
      foreach (var entry in _trail)
      {
        trail.Add(new CapturedTrailEntry
        {
          Index = entry.Index,
          EnvironmentId = GetId(entry.Environment),
          PendingSignal = entry.PendingSignal != null ? CapturedSignal.From(entry.PendingSignal.Value) : null,
        });
      }

      if (!moduleKeysByEnvironment.TryGetValue(_rootEnvironment!, out string? rootModuleKey) || !rootModuleKey.StartsWith("module:", StringComparison.Ordinal))
      {
        throw new InvalidOperationException("EvaluationCursor.CaptureStructural: the root environment is not a module environment.");
      }

      CapturedAwaitElement CaptureAwaitElement(AwaitElement element)
      {
        if (element.Resolved != null)
        {
          return new CapturedAwaitElement.Resolved(CaptureValue(element.Resolved), element.ElementType);
        }

        if (element.ReplayedFaultMessage != null)
        {
          return new CapturedAwaitElement.Fault(element.ReplayedFaultMessage, element.ElementType);
        }

        if (element.Task != null)
        {
          if (element.Task.Status == TaskStatus.RanToCompletion)
          {
            return new CapturedAwaitElement.Resolved(CaptureValue(element.Task.Result), element.ElementType);
          }

          if (element.Task.IsFaulted)
          {
            var fault = element.Task.Exception?.InnerException ?? element.Task.Exception;
            return new CapturedAwaitElement.Fault(fault?.Message ?? "Unknown error", element.ElementType);
          }

          if (element.Operation != null)
          {
            return new CapturedAwaitElement.Reissue(element.Operation, element.ElementType);
          }

          throw new NotSupportedException(
            "Capturing a composite 'await [a, b, c]' element that is a live operation not backed by a " +
            "'thunk' native operation descriptor (AwaitElement.Operation) is not yet supported by structural " +
            "Capture/Restore (docs/ASYNC_AWAIT_DESIGN.md Addendum 3).");
        }

        throw new NotSupportedException(
          "Capturing a composite 'await [a, b, c]' element in an unrecognized state is not supported by " +
          "structural Capture/Restore (docs/ASYNC_AWAIT_DESIGN.md Addendum 3).");
      }

      CapturedPendingAwait capturedPendingAwait;
      if (compositeElementsToCapture != null)
      {
        var capturedElements = new List<CapturedAwaitElement>();
        foreach (var element in compositeElementsToCapture)
        {
          capturedElements.Add(CaptureAwaitElement(element));
        }

        capturedPendingAwait = new CapturedPendingAwait
        {
          Site = pending.Site,
          CompositeElements = capturedElements,
        };
      }
      else
      {
        capturedPendingAwait = new CapturedPendingAwait
        {
          Operation = pending.Operation,
          ElementType = pending.ElementType,
          Site = pending.Site,
        };
      }

      return new CursorStructuralCaptureState
      {
        ModuleKey = rootModuleKey,
        Heap = heap,
        StaticFields = staticFields,
        Environments = environments,
        Trail = trail,
        RootEnvironmentId = rootEnvironmentId,
        Signal = Signal != null ? CapturedSignal.From(Signal.Value) : null,
        PendingAwait = capturedPendingAwait,
      };
    }

    /// <summary>
    /// Grafts a "Phase B" structural snapshot's trail/environments/signal/
    /// pending-await onto this cursor, which must have just finished running
    /// each module's declaration prefix (<see cref="ModuleDeclarationPrefix.GetDeclarationPrefix"/>)
    /// via <see cref="CursorProgramEvaluator.RestoreStructural"/>. Leaves this
    /// cursor in the same state as if <see cref="Run"/> had just returned
    /// <see cref="RunResult.Awaiting"/> (or <see cref="RunResult.Completed"/>
    /// if <paramref name="state"/> has no <see cref="CursorStructuralCaptureState.PendingAwait"/>),
    /// ready for <see cref="Resume"/>/<see cref="ResumeFaulted"/>.
    ///
    /// <paramref name="moduleEnvironments"/> maps each
    /// <see cref="CapturedEnvironment.ModuleRef"/>'s <c>ModuleKey</c> to the
    /// <see cref="ScriptEnvironment"/> the declaration-prefix run produced —
    /// reused as-is (its bindings overwritten/extended from the captured
    /// snapshot) rather than re-allocated.
    /// </summary>
    public RunResult RestoreSuspendedState(CursorStructuralCaptureState state, IReadOnlyDictionary<string, ScriptEnvironment> moduleEnvironments, ModuleGraph graph, IAsyncOperationBinder? binder)
    {
      var environments = new ScriptEnvironment?[state.Environments.Count];

      ALKScriptValue ResolveAstRef(AstReference reference)
      {
        string moduleKey = reference.ModuleKey.StartsWith("prelude:", StringComparison.Ordinal) ? "globals" : reference.ModuleKey;

        if (!moduleEnvironments.TryGetValue(moduleKey, out var moduleEnv))
        {
          throw new KeyNotFoundException($"RestoreSuspendedState: no module environment for '{moduleKey}' (resolving '{reference}').");
        }

        var segments = reference.Path.Split('.');
        if (segments.Length != 1)
        {
          throw new NotSupportedException(
            $"RestoreSuspendedState: AstReference '{reference}' addresses a class/enum member, method, or lambda — " +
            "only references to top-level declarations are supported by structural Capture/Restore's current milestone " +
            "(docs/ASYNC_AWAIT_DESIGN.md Addendum 3, Step 9+).");
        }

        if (!moduleEnv.TryGet(segments[0], out var value))
        {
          throw new KeyNotFoundException($"RestoreSuspendedState: '{moduleKey}' has no top-level binding named '{segments[0]}' (resolving '{reference}').");
        }

        return value;
      }

      var heapValues = new ALKScriptValue?[state.Heap.Count];

      // Pass 1: create empty InstanceValue placeholders for Instance entries, so cyclic field references resolve below.
      for (int i = 0; i < state.Heap.Count; i++)
      {
        if (state.Heap[i] is CapturedHeapEntry.Instance instanceEntry)
        {
          var classValue = (ClassValue)ResolveAstRef(instanceEntry.ClassRef);
          heapValues[i] = new InstanceValue(classValue, instanceEntry.TypeArguments);
        }
      }

      // Pass 2: create BaseValue entries, referencing the instances created above.
      for (int i = 0; i < state.Heap.Count; i++)
      {
        if (state.Heap[i] is CapturedHeapEntry.Base baseEntry)
        {
          var superclass = (ClassValue)ResolveAstRef(baseEntry.SuperclassRef);
          heapValues[i] = new BaseValue(superclass, (InstanceValue)heapValues[baseEntry.InstanceId]!);
        }
      }

      ALKScriptValue ResolveHeapValue(CapturedHeapValue captured) => captured switch
      {
        CapturedHeapValue.Primitive primitive => primitive.Value,
        CapturedHeapValue.AstRef astRef => ResolveAstRef(astRef.Reference),
        CapturedHeapValue.HeapRef heapRef => heapValues[heapRef.Id]!,
        CapturedHeapValue.Method method => ResolveMethod(method),
        _ => throw new NotSupportedException($"RestoreSuspendedState: unrecognized captured value type '{captured.GetType().Name}'."),
      };

      ALKScriptValue ResolveMethod(CapturedHeapValue.Method method)
      {
        var methodDecl = (MethodDecl)AstResolver.Resolve(graph, method.Reference);

        int lastDot = method.Reference.Path.LastIndexOf('.');
        var classRef = new AstReference(method.Reference.ModuleKey, method.Reference.Path.Substring(0, lastDot));
        var declaringClass = (ClassValue)ResolveAstRef(classRef);
        var boundInstance = (InstanceValue)ResolveHeapValue(method.Instance);

        return FunctionValueFactory.CreateMethod(methodDecl, declaringClass, declaringClass.Closure, boundInstance);
      }

      // Pass 3: fill instance fields, now that every instance/base in the heap exists (handles cycles).
      for (int i = 0; i < state.Heap.Count; i++)
      {
        if (state.Heap[i] is CapturedHeapEntry.Instance instanceEntry)
        {
          var instance = (InstanceValue)heapValues[i]!;
          foreach (var field in instanceEntry.Fields)
          {
            instance.Fields[field.Key] = ResolveHeapValue(field.Value);
          }
        }
      }

      // Graft captured ClassValue.StaticFields onto the ClassValues the declaration-prefix run re-created.
      foreach (var entry in state.StaticFields)
      {
        var classValue = (ClassValue)ResolveAstRef(entry.ClassRef);
        foreach (var field in entry.Fields)
        {
          classValue.StaticFields[field.Key] = ResolveHeapValue(field.Value);
        }
      }

      ScriptEnvironment Build(int id)
      {
        if (environments[id] != null)
        {
          return environments[id]!;
        }

        var captured = state.Environments[id];

        ScriptEnvironment env;
        if (captured.ModuleRef != null)
        {
          if (!moduleEnvironments.TryGetValue(captured.ModuleRef.ModuleKey, out env!))
          {
            throw new KeyNotFoundException($"RestoreSuspendedState: no module environment for '{captured.ModuleRef.ModuleKey}'.");
          }
        }
        else
        {
          var enclosing = captured.EnclosingId.HasValue ? Build(captured.EnclosingId.Value) : null;
          env = new ScriptEnvironment(enclosing);
        }

        environments[id] = env;

        foreach (var binding in captured.Values)
        {
          var value = ResolveHeapValue(binding.Value);

          var declaredType = captured.Types.TryGetValue(binding.Key, out var type) ? type : null;
          env.Define(binding.Key, value, declaredType, captured.Consts.Contains(binding.Key));
        }

        env.CurrentFunctionReturnType = captured.CurrentFunctionReturnType;
        env.CurrentTypeArguments = captured.CurrentTypeArguments;
        env.IsInConstructor = captured.IsInConstructor;

        if (captured.CurrentClass != null)
        {
          env.CurrentClass = (ClassValue)ResolveAstRef(captured.CurrentClass.Reference);
        }

        return env;
      }

      for (int id = 0; id < state.Environments.Count; id++)
      {
        Build(id);
      }

      _rootEnvironment = environments[state.RootEnvironmentId]!;
      _rootStatements = AstResolver.GetProgram(graph, state.ModuleKey).Declarations;

      _trail = new List<TrailEntry>(state.Trail.Count);
      foreach (var entry in state.Trail)
      {
        _trail.Add(new TrailEntry(entry.Index, environments[entry.EnvironmentId]!, entry.PendingSignal?.ToSignal()));
      }

      _newTrail = new List<TrailEntry>();
      _resumeCursor = -1;
      _resumeValue = null;
      _resumeComposite = null;

      Signal = state.Signal?.ToSignal();

      if (state.PendingAwait == null)
      {
        PendingAwait = null;
        return RunResult.Completed;
      }

      if (binder == null)
      {
        throw new InvalidOperationException(
          "RestoreSuspendedState: the captured state has a pending operation, but no IAsyncOperationBinder was provided to restart it.");
      }

      AwaitElement ReissueElement(CapturedAwaitElement.Reissue reissue)
      {
        var reissueTask = binder.Start(reissue.Operation);
        var reissuePending = new PendingOperationValue(reissue.Operation, binder, reissue.ElementType);
        reissuePending.MarkStarted(reissueTask);
        FunctionValueFactory.RegisterRestored(reissuePending);
        return AwaitElement.ForTask(reissueTask, reissue.ElementType, reissue.Operation);
      }

      if (state.PendingAwait.CompositeElements != null)
      {
        var elements = new List<AwaitElement>();
        foreach (var captured in state.PendingAwait.CompositeElements)
        {
          elements.Add(captured switch
          {
            CapturedAwaitElement.Resolved resolved => AwaitElement.ForResolved(ResolveHeapValue(resolved.Value), resolved.ElementType),
            CapturedAwaitElement.Reissue reissue => ReissueElement(reissue),
            CapturedAwaitElement.Fault fault => AwaitElement.ForReplayedFault(fault.Message, fault.ElementType),
            _ => throw new NotSupportedException($"RestoreSuspendedState: unrecognized captured await element type '{captured.GetType().Name}'."),
          });
        }

        PendingAwait = AwaitHandle.ForComposite(elements, state.PendingAwait.Site);
        return RunResult.Awaiting;
      }

      var operation = state.PendingAwait.Operation!;
      var task = binder.Start(operation);
      var pendingValue = new PendingOperationValue(operation, binder, state.PendingAwait.ElementType);
      pendingValue.MarkStarted(task);
      FunctionValueFactory.RegisterRestored(pendingValue);
      PendingAwait = AwaitHandle.ForPendingTask(task, operation, state.PendingAwait.ElementType, state.PendingAwait.Site);
      return RunResult.Awaiting;
    }

    /// <summary>
    /// The pending non-local exit (break/continue/return/thrown/cancelled), if
    /// any. Mirrors <see cref="IEvaluationContext.Signal"/> — set by
    /// statement-level constructs and checked by every caller after each
    /// sub-step.
    /// </summary>
    public Signal? Signal { get; set; }

    /// <summary>What <see cref="Resume"/>/<see cref="ResumeFaulted"/> will settle, while <see cref="RunResult.Awaiting"/>.</summary>
    public AwaitHandle? PendingAwait { get; private set; }

    /// <summary>Whether a resumable construct should fast-forward into a previously-recorded child instead of starting fresh.</summary>
    public bool IsResuming => _resumeCursor >= 0;

    /// <summary>Whether the next <see cref="AwaitExpr"/> reached should consume <see cref="TakeResumeValue"/> instead of evaluating its operand.</summary>
    public bool HasResumeValue => _resumeValue != null;

    /// <summary>
    /// Returns the environment of the next (root-to-leaf) entry of the resume
    /// trail without consuming it, or <c>null</c> if not <see cref="IsResuming"/>.
    /// Used by <see cref="CursorCallInvoker.Construct"/> to recover the
    /// <c>this</c> instance a suspended constructor body is about to resume
    /// into, before <see cref="ExecuteBlock"/> itself pops the entry.
    /// </summary>
    public ScriptEnvironment? PeekResumeEnvironment() => IsResuming ? _trail[_resumeCursor].Environment : null;

    /// <summary>
    /// Pops and returns the next (root-to-leaf) entry of the resume trail.
    /// Only valid while <see cref="IsResuming"/> is true.
    /// </summary>
    public TrailEntry PopResumeEntry()
    {
      var entry = _trail[_resumeCursor];
      _resumeCursor--;
      return entry;
    }

    /// <summary>
    /// Records that the construct currently executing child <paramref name="index"/>
    /// (with <paramref name="environment"/>) is suspending — called by every
    /// resumable construct on the way back up from a child that returned
    /// <see cref="StepResult.IsAwaiting"/>. Only called in "normal" (not
    /// resuming) mode, so entries accumulate leaf-first.
    /// </summary>
    public void RecordSuspend(int index, ScriptEnvironment environment)
    {
      _newTrail.Add(new TrailEntry(index, environment));
    }

    /// <summary>As <see cref="RecordSuspend(int, ScriptEnvironment)"/>, additionally persisting a <c>try</c>/<c>finally</c>'s pending signal.</summary>
    public void RecordSuspend(int index, ScriptEnvironment environment, Signal? pendingSignal)
    {
      _newTrail.Add(new TrailEntry(index, environment, pendingSignal));
    }

    /// <summary>
    /// Consumes and returns the value <see cref="Resume"/> was called with —
    /// the result of the <c>await</c> expression that suspended. Must be
    /// called at most once per resume (enforced implicitly: the leaf
    /// statement's single top-level <c>await</c>).
    /// </summary>
    public ALKScriptValue TakeResumeValue()
    {
      var value = _resumeValue ?? NullValue.Instance;
      _resumeValue = null;
      return value;
    }

    /// <summary>
    /// If a composite <c>await [a, b, c]</c> just resumed, consumes and
    /// returns its <see cref="AwaitElement"/>s (whose tasks are now all
    /// settled) and returns <c>true</c>. Must be called at most once per
    /// resume, mirroring <see cref="TakeResumeValue"/>.
    /// </summary>
    public bool TryTakeResumeComposite(out IReadOnlyList<AwaitElement> elements)
    {
      if (_resumeComposite != null)
      {
        elements = _resumeComposite;
        _resumeComposite = null;
        return true;
      }

      elements = System.Array.Empty<AwaitElement>();
      return false;
    }

    /// <summary>
    /// Evaluates <paramref name="expression"/> against <paramref name="environment"/>.
    /// Returns <see cref="StepResult.Completed"/> with <see cref="NullValue.Instance"/>
    /// without recursing if <see cref="Signal"/> is already set, mirroring the
    /// old evaluator's "if (_context.Signal != null) return NullValue.Instance;"
    /// guard at the top of <c>Eval</c>.
    /// </summary>
    public StepResult Eval(Expr expression, ScriptEnvironment environment) => Eval(expression, environment, allowSuspend: false);

    /// <summary>
    /// As <see cref="Eval(Expr, ScriptEnvironment)"/>, but additionally allows
    /// an <see cref="AwaitExpr"/> directly at <paramref name="expression"/> to
    /// return <see cref="StepResult.Awaiting"/> on an unresolved thunk — used
    /// for the await-placement-restricted positions of plan §4 (the entire
    /// initializer of a <see cref="VariableDecl"/>, the entire value of a
    /// <see cref="ReturnStmt"/>/<see cref="ThrowStmt"/>, or the entire
    /// expression of an <see cref="ExpressionStmt"/>).
    /// </summary>
    public StepResult Eval(Expr expression, ScriptEnvironment environment, bool allowSuspend)
    {
      if (Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      return _expressionEvaluator.Eval(expression, environment, allowSuspend);
    }

    /// <summary>
    /// Executes <paramref name="statement"/> against <paramref name="environment"/>.
    /// Mirrors the no-op short-circuit in <see cref="Eval"/>: if
    /// <see cref="Signal"/> is already set, returns immediately without
    /// recursing.
    /// </summary>
    public StepResult Execute(Stmt statement, ScriptEnvironment environment)
    {
      if (Signal != null)
      {
        return StepResult.Completed(NullValue.Instance);
      }

      return _statementExecutor.Execute(statement, environment);
    }

    /// <summary>Executes each statement in order, stopping early on suspension or a pending <see cref="Signal"/>.</summary>
    public StepResult ExecuteBlock(IReadOnlyList<Stmt> statements, ScriptEnvironment environment)
    {
      return _statementExecutor.ExecuteBlock(statements, environment);
    }

    /// <summary>Calls <paramref name="callee"/> with <paramref name="arguments"/>.</summary>
    public StepResult Call(ALKScriptValue callee, IReadOnlyList<ALKScriptValue> arguments, ALKScriptToken site)
    {
      return _callInvoker.Call(callee, arguments, site);
    }

    /// <summary>Constructs a new instance of <paramref name="classValue"/>.</summary>
    public StepResult Construct(ClassValue classValue, IReadOnlyList<ALKScriptValue> arguments, IReadOnlyList<TypeNode> typeArguments, ALKScriptToken site)
    {
      return _callInvoker.Construct(classValue, arguments, typeArguments, site);
    }

    /// <summary>
    /// Begins evaluating <paramref name="statements"/> against <paramref name="environment"/>
    /// as the outermost (top-level) sequence — the only level at which
    /// suspension is currently supported. Returns <see cref="RunResult.Awaiting"/>
    /// if execution hits an unresolved <c>await</c> in an allowed position
    /// (see <see cref="PendingAwait"/>), otherwise <see cref="RunResult.Completed"/>.
    /// </summary>
    public RunResult Start(IReadOnlyList<Stmt> statements, ScriptEnvironment environment)
    {
      _rootStatements = statements;
      _rootEnvironment = environment;
      return Run();
    }

    private RunResult Run()
    {
      var step = ExecuteBlock(_rootStatements!, _rootEnvironment!);

      if (step.IsAwaiting)
      {
        PendingAwait = step.Handle;
        _trail = _newTrail;
        _newTrail = new List<TrailEntry>();
        return RunResult.Awaiting;
      }

      PendingAwait = null;
      _trail.Clear();
      _newTrail.Clear();
      _resumeCursor = -1;
      return RunResult.Completed;
    }

    /// <summary>
    /// Resumes a suspended run with the settled result of the pending
    /// <c>await</c> — validated against <see cref="AwaitHandle.ElementType"/>,
    /// per <see cref="TypeChecking.MatchesType"/>, exactly as the old
    /// evaluator's <c>ValidateThunkResult</c> did.
    /// </summary>
    public RunResult Resume(ALKScriptValue value)
    {
      var pending = PendingAwait ?? throw new InvalidOperationException("EvaluationCursor.Resume called while not awaiting.");

      if (pending.CompositeElements != null)
      {
        _resumeComposite = pending.CompositeElements;
        _resumeCursor = _trail.Count - 1;
        PendingAwait = null;
        return Run();
      }

      if (pending.ElementType != null && !TypeChecking.MatchesType(value, pending.ElementType, _rootEnvironment!, pending.Site))
      {
        throw new RuntimeException(pending.Site, $"Operation declared 'thunk<{pending.ElementType}>' resolved to a value of type '{value.TypeName}', expected '{pending.ElementType}'.");
      }

      if (pending.Operation != null)
      {
        _log.Add(OperationLogEntry.FromResult(pending.Operation, value));
      }

      _resumeValue = value;
      _resumeCursor = _trail.Count - 1;
      PendingAwait = null;
      return Run();
    }

    /// <summary>
    /// Resumes a suspended run by raising <paramref name="faultMessage"/> as a
    /// <see cref="SignalKind.Thrown"/> signal at the point of suspension —
    /// mirroring the old evaluator's "catch (Exception) { Signal =
    /// Signal.Thrown(...) }" around <c>AwaitTask</c>/<c>AwaitPending</c>.
    /// </summary>
    public RunResult ResumeFaulted(string faultMessage)
    {
      if (PendingAwait?.CompositeElements != null)
      {
        throw new InvalidOperationException("ResumeFaulted is not valid for a composite 'await [...]' suspension — await PendingAwait.CompositeTask (which settles only once every element has completed) and call Resume(NullValue.Instance) instead.");
      }

      if (PendingAwait?.Operation != null)
      {
        _log.Add(OperationLogEntry.FromFault(PendingAwait.Operation, faultMessage));
      }

      PendingAwait = null;
      Signal = ALKScript.Interpreter.Common.Evaluation.Signal.Thrown(new StringValue(faultMessage));
      _resumeValue = NullValue.Instance;
      _resumeCursor = _trail.Count - 1;
      return Run();
    }
  }
}
