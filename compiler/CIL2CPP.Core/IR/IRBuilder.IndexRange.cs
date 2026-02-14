using Mono.Cecil;

namespace CIL2CPP.Core.IR;

/// <summary>
/// System.Index and System.Range method interception.
/// These are BCL value types whose method bodies are not in user assemblies.
/// We intercept calls and emit inline C++ instead.
///
/// System.Index internal representation:
///   int _value — bit 31 indicates fromEnd (stored as ~value when fromEnd=true)
///
/// System.Range internal representation:
///   Index _start, _end — start and end indices
/// </summary>
public partial class IRBuilder
{
    /// <summary>
    /// Create synthetic IRTypes for System.Index and System.Range.
    /// These BCL value types are not in user assemblies but are referenced by IL.
    /// </summary>
    private void CreateIndexRangeSyntheticTypes()
    {
        // Only create if not already present (multi-assembly mode might have them)
        if (!_typeCache.ContainsKey("System.Index"))
        {
            var indexType = new IRType
            {
                ILFullName = "System.Index",
                CppName = "System_Index",
                Name = "Index",
                Namespace = "System",
                IsValueType = true,
                IsSealed = true,
            };
            indexType.Fields.Add(new IRField
            {
                Name = "_value",
                CppName = "f__value",
                FieldTypeName = "System.Int32",
                IsStatic = false,
                IsPublic = false,
                DeclaringType = indexType,
            });
            _module.Types.Add(indexType);
            _typeCache["System.Index"] = indexType;
        }

        if (!_typeCache.ContainsKey("System.Range"))
        {
            var rangeType = new IRType
            {
                ILFullName = "System.Range",
                CppName = "System_Range",
                Name = "Range",
                Namespace = "System",
                IsValueType = true,
                IsSealed = true,
            };
            rangeType.Fields.Add(new IRField
            {
                Name = "_start",
                CppName = "f__start",
                FieldTypeName = "System.Index",
                IsStatic = false,
                IsPublic = false,
                DeclaringType = rangeType,
            });
            rangeType.Fields.Add(new IRField
            {
                Name = "_end",
                CppName = "f__end",
                FieldTypeName = "System.Index",
                IsStatic = false,
                IsPublic = false,
                DeclaringType = rangeType,
            });
            _module.Types.Add(rangeType);
            _typeCache["System.Range"] = rangeType;
        }
    }

    /// <summary>
    /// Handle calls to System.Index methods by emitting inline C++.
    /// Returns true if the call was handled.
    /// </summary>
    private bool TryEmitIndexCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (methodRef.DeclaringType.FullName != "System.Index") return false;

        // Wrap thisArg for ldloca pattern: &loc_0 → (&loc_0)->
        string This()
        {
            var raw = stack.Count > 0 ? stack.Pop() : "nullptr";
            return raw.StartsWith("&") ? $"({raw})" : raw;
        }

        switch (methodRef.Name)
        {
            case ".ctor":
            {
                // Index(int value, bool fromEnd)
                // Stack: [thisAddr, value, fromEnd]
                var fromEnd = stack.Count > 0 ? stack.Pop() : "false";
                var value = stack.Count > 0 ? stack.Pop() : "0";
                var thisArg = This();
                // ECMA: _value = fromEnd ? ~value : value
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{thisArg}->f__value = {fromEnd} ? ~{value} : {value};"
                });
                return true;
            }
            case "GetOffset":
            {
                // int GetOffset(int length) — instance method
                // Stack: [thisAddr, length]
                // ECMA: offset = _value < 0 ? _value + length + 1 : _value
                var length = stack.Count > 0 ? stack.Pop() : "0";
                var thisArg = This();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"int32_t {tmp} = {thisArg}->f__value < 0 " +
                           $"? {thisArg}->f__value + {length} + 1 " +
                           $": {thisArg}->f__value;"
                });
                stack.Push(tmp);
                return true;
            }
            case "get_Value":
            {
                // int Value { get; } — instance property
                // ECMA: _value < 0 ? ~_value : _value
                var thisArg = This();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {thisArg}->f__value < 0 " +
                           $"? ~{thisArg}->f__value : {thisArg}->f__value;"
                });
                stack.Push(tmp);
                return true;
            }
            case "get_IsFromEnd":
            {
                // bool IsFromEnd { get; } — instance property
                // ECMA: _value < 0
                var thisArg = This();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {thisArg}->f__value < 0;"
                });
                stack.Push(tmp);
                return true;
            }
            case "op_Implicit":
            {
                // static implicit operator Index(int value)
                // Static method — no this pointer
                // Stack: [value]
                var value = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                // fromEnd = false → _value = value (no bit manipulation)
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"System_Index {tmp}; {tmp}.f__value = {value};"
                });
                stack.Push(tmp);
                return true;
            }
            case "FromStart":
            {
                // static Index FromStart(int value) — same as op_Implicit
                var value = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"System_Index {tmp}; {tmp}.f__value = {value};"
                });
                stack.Push(tmp);
                return true;
            }
            case "FromEnd":
            {
                // static Index FromEnd(int value) — creates fromEnd index
                var value = stack.Count > 0 ? stack.Pop() : "0";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"System_Index {tmp}; {tmp}.f__value = ~{value};"
                });
                stack.Push(tmp);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Handle calls to System.Range methods by emitting inline C++.
    /// Returns true if the call was handled.
    /// </summary>
    private bool TryEmitRangeCall(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (methodRef.DeclaringType.FullName != "System.Range") return false;

        string This()
        {
            var raw = stack.Count > 0 ? stack.Pop() : "nullptr";
            return raw.StartsWith("&") ? $"({raw})" : raw;
        }

        switch (methodRef.Name)
        {
            case ".ctor":
            {
                // Range(Index start, Index end) — via ldloca+call pattern
                // Stack: [thisAddr, start, end]
                var end = stack.Count > 0 ? stack.Pop() : "{}";
                var start = stack.Count > 0 ? stack.Pop() : "{}";
                var thisArg = This();
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{thisArg}->f__start = {start}; {thisArg}->f__end = {end};"
                });
                return true;
            }
            case "get_Start":
            {
                // Index Start { get; } — instance property
                var thisArg = This();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {thisArg}->f__start;"
                });
                stack.Push(tmp);
                return true;
            }
            case "get_End":
            {
                // Index End { get; } — instance property
                var thisArg = This();
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"auto {tmp} = {thisArg}->f__end;"
                });
                stack.Push(tmp);
                return true;
            }
            case "GetOffsetAndLength":
            {
                // (int Offset, int Length) GetOffsetAndLength(int length) — instance method
                // Returns ValueTuple<int, int>
                // Stack: [thisAddr, length]
                var length = stack.Count > 0 ? stack.Pop() : "0";
                var thisArg = This();
                var startTmp = $"__t{tempCounter++}";
                var endTmp = $"__t{tempCounter++}";
                var tmp = $"__t{tempCounter++}";

                // Compute start offset from Index
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"int32_t {startTmp} = {thisArg}->f__start.f__value < 0 " +
                           $"? {thisArg}->f__start.f__value + {length} + 1 " +
                           $": {thisArg}->f__start.f__value;"
                });
                // Compute end offset from Index
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"int32_t {endTmp} = {thisArg}->f__end.f__value < 0 " +
                           $"? {thisArg}->f__end.f__value + {length} + 1 " +
                           $": {thisArg}->f__end.f__value;"
                });

                // Construct ValueTuple<int, int> with (offset, length)
                var vtKey = "System.ValueTuple`2<System.Int32,System.Int32>";
                string vtCppName;
                if (_typeCache.TryGetValue(vtKey, out var vtType))
                    vtCppName = vtType.CppName;
                else
                    vtCppName = CppNameMapper.MangleTypeName(vtKey);

                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"{vtCppName} {tmp}; {tmp}.f_Item1 = {startTmp}; " +
                           $"{tmp}.f_Item2 = {endTmp} - {startTmp};"
                });
                stack.Push(tmp);
                return true;
            }
            case "get_All":
            {
                // static Range All { get; } — returns Range(Index.Start, Index.End)
                // No stack arguments (static property)
                var tmp = $"__t{tempCounter++}";
                // Index.Start = {_value: 0}, Index.End = {_value: ~0 = -1}
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"System_Range {tmp}; " +
                           $"{tmp}.f__start.f__value = 0; " +
                           $"{tmp}.f__end.f__value = ~0;"
                });
                stack.Push(tmp);
                return true;
            }
            case "StartAt":
            {
                // static Range StartAt(Index start) — returns Range(start, Index.End)
                var start = stack.Count > 0 ? stack.Pop() : "{}";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"System_Range {tmp}; " +
                           $"{tmp}.f__start = {start}; " +
                           $"{tmp}.f__end.f__value = ~0;"
                });
                stack.Push(tmp);
                return true;
            }
            case "EndAt":
            {
                // static Range EndAt(Index end) — returns Range(Index.Start, end)
                var end = stack.Count > 0 ? stack.Pop() : "{}";
                var tmp = $"__t{tempCounter++}";
                block.Instructions.Add(new IRRawCpp
                {
                    Code = $"System_Range {tmp}; " +
                           $"{tmp}.f__start.f__value = 0; " +
                           $"{tmp}.f__end = {end};"
                });
                stack.Push(tmp);
                return true;
            }
            default:
                return false;
        }
    }

    /// <summary>
    /// Handle newobj for System.Index.
    /// Returns true if handled.
    /// </summary>
    private bool TryEmitIndexNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        if (ctorRef.DeclaringType.FullName != "System.Index") return false;

        var tmp = $"__t{tempCounter++}";

        if (ctorRef.Parameters.Count == 2)
        {
            // Index(int value, bool fromEnd)
            var fromEnd = stack.Count > 0 ? stack.Pop() : "false";
            var value = stack.Count > 0 ? stack.Pop() : "0";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"System_Index {tmp}; {tmp}.f__value = {fromEnd} ? ~{value} : {value};"
            });
        }
        else if (ctorRef.Parameters.Count == 1)
        {
            // Index(int value) — fromEnd defaults to false
            var value = stack.Count > 0 ? stack.Pop() : "0";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"System_Index {tmp}; {tmp}.f__value = {value};"
            });
        }
        else
        {
            // Default ctor — zero-initialize
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"System_Index {tmp} = {{}};"
            });
        }
        stack.Push(tmp);
        return true;
    }

    /// <summary>
    /// Handle newobj for System.Range.
    /// Returns true if handled.
    /// </summary>
    private bool TryEmitRangeNewObj(IRBasicBlock block, Stack<string> stack,
        MethodReference ctorRef, ref int tempCounter)
    {
        if (ctorRef.DeclaringType.FullName != "System.Range") return false;

        var tmp = $"__t{tempCounter++}";

        if (ctorRef.Parameters.Count == 2)
        {
            // Range(Index start, Index end)
            var end = stack.Count > 0 ? stack.Pop() : "{}";
            var start = stack.Count > 0 ? stack.Pop() : "{}";
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"System_Range {tmp}; {tmp}.f__start = {start}; {tmp}.f__end = {end};"
            });
        }
        else
        {
            // Default ctor — zero-initialize
            block.Instructions.Add(new IRRawCpp
            {
                Code = $"System_Range {tmp} = {{}};"
            });
        }
        stack.Push(tmp);
        return true;
    }

    /// <summary>
    /// Handle RuntimeHelpers.GetSubArray&lt;T&gt;(T[], Range) call.
    /// Returns true if handled.
    /// </summary>
    private bool TryEmitGetSubArray(IRBasicBlock block, Stack<string> stack,
        MethodReference methodRef, ref int tempCounter)
    {
        if (methodRef.DeclaringType.FullName != "System.Runtime.CompilerServices.RuntimeHelpers")
            return false;
        if (methodRef.Name != "GetSubArray")
            return false;

        // GetSubArray<T>(T[] array, Range range)
        // Stack: [array, range]
        var range = stack.Count > 0 ? stack.Pop() : "{}";
        var arr = stack.Count > 0 ? stack.Pop() : "nullptr";

        var startTmp = $"__t{tempCounter++}";
        var endTmp = $"__t{tempCounter++}";
        var lenTmp = $"__t{tempCounter++}";
        var tmp = $"__t{tempCounter++}";

        // Compute start offset
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"int32_t {startTmp} = {range}.f__start.f__value < 0 " +
                   $"? {range}.f__start.f__value + cil2cpp::array_length({arr}) + 1 " +
                   $": {range}.f__start.f__value;"
        });
        // Compute end offset
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"int32_t {endTmp} = {range}.f__end.f__value < 0 " +
                   $"? {range}.f__end.f__value + cil2cpp::array_length({arr}) + 1 " +
                   $": {range}.f__end.f__value;"
        });
        // Compute slice length
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"int32_t {lenTmp} = {endTmp} - {startTmp};"
        });
        // Create new array and copy elements
        block.Instructions.Add(new IRRawCpp
        {
            Code = $"auto {tmp} = cil2cpp::array_get_subarray({arr}, {startTmp}, {lenTmp});"
        });

        stack.Push(tmp);
        return true;
    }
}
