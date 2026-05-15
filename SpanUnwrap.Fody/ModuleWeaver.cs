using System;
using System.Collections.Generic;
using System.Linq;

using Fody;

using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Mono.Collections.Generic;

public class ModuleWeaver : BaseModuleWeaver
{
    private readonly record struct MethodSignature(TypeReference ReturnType, TypeReference[] ParameterTypes, TypeReference[] GenericArguments);

    private enum MethodIndex
    {
        None,
        Unwrap_Span,
        Unwrap_ReadOnlySpan
    }

    private enum SpanTypeIndex
    {
        None,
        Span,
        ReadOnlySpan
    }

    public override IEnumerable<string> GetAssembliesForScanning() => [];

    public override bool ShouldCleanReference => true;

    public override void Execute()
    {
        WriteMessage("SpanUnwrap execute", MessageImportance.High);

        foreach (TypeDefinition type in ModuleDefinition.GetTypes())
        {
            foreach (MethodDefinition method in type.Methods)
            {
                TryProcessMethod(method);
            }
        }
    }

    private void TryProcessMethod(MethodDefinition method)
    {
        MethodBody? body = method.Body;
        if (body is null)
            return;
        Instruction? instruction = body.Instructions.FirstOrDefault();
        if (instruction is null)
            return;
        do
        {
            Instruction nextInstruction = instruction.Next;
            if (instruction.OpCode != OpCodes.Call || instruction.Operand is not MethodReference callee)
                goto Tail;
            switch (GetMethodIndex(callee, out MethodSignature signature))
            {
                case MethodIndex.Unwrap_Span:
                case MethodIndex.Unwrap_ReadOnlySpan:
                    UnwrapSpan(body, instruction, signature);
                    goto Tail;
                default:
                    goto Tail;
            }

        Tail:
            instruction = nextInstruction;
        } while (instruction is not null);
    }

    private MethodIndex GetMethodIndex(MethodReference method, out MethodSignature signature)
    {
        MethodDefinition? definition = method.Resolve();
        if (definition is null ||
            !definition.HasParameters ||
            !definition.HasGenericParameters ||
            !definition.IsStatic ||
            !string.Equals(definition.Name, "From") ||
            !string.Equals(definition.DeclaringType.FullName, "SpanUnwrap.Unwrap"))
            goto Failed;

        Collection<GenericParameter> genericParameters = definition.GenericParameters;
        Collection<ParameterDefinition> parameters = definition.Parameters;
        if (genericParameters.Count != 1 || parameters.Count != 1)
            goto Failed;
        GenericParameter genericParameter = genericParameters[0];
        TypeReference returnType = definition.ReturnType;
        while (returnType is TypeSpecification typeSpec && typeSpec is not ByReferenceType && typeSpec is not GenericInstanceType)
            returnType = typeSpec.ElementType;
        if (!returnType.IsByReference)
            goto Failed;
        returnType = returnType.GetElementType();
        while (returnType is TypeSpecification innerSpec && innerSpec is not GenericInstanceType)
            returnType = innerSpec.ElementType;
        if (returnType != genericParameter)
            goto Failed;

        ParameterDefinition parameter = parameters[0];
        TypeReference parameterType = parameter.ParameterType;
        if (parameterType.IsByReference ||
            parameterType is not GenericInstanceType instanceType)
            goto Failed;

        if (method is GenericInstanceMethod genericInstanceMethod)
            signature = new MethodSignature(method.ReturnType,
                method.Parameters.Select(p => p.ParameterType).ToArray(),
                genericInstanceMethod.GenericArguments.ToArray());
        else
            signature = new MethodSignature(method.ReturnType,
                method.Parameters.Select(p => p.ParameterType).ToArray(),
                []);

        return GetSpanTypeIndexForType(instanceType, genericParameter) switch
        {
            SpanTypeIndex.Span => MethodIndex.Unwrap_Span,
            SpanTypeIndex.ReadOnlySpan => MethodIndex.Unwrap_ReadOnlySpan,
            _ => MethodIndex.None
        };

    Failed:
        signature = default;
        return MethodIndex.None;
    }

    private static SpanTypeIndex GetSpanTypeIndexForType(GenericInstanceType type, GenericParameter typeParam)
    {
        TypeDefinition? definition = type.Resolve();
        if (definition is null ||
            !definition.HasGenericParameters ||
            !definition.IsValueType ||
            !string.Equals(definition.Namespace, "System"))
            goto Failed;

        switch (definition.Name)
        {
            case "Span`1":
                if (type.GenericArguments.Count != 1 || type.GenericArguments[0] != typeParam)
                    goto Failed;
                return SpanTypeIndex.Span;
            case "ReadOnlySpan`1":
                if (type.GenericArguments.Count != 1 || type.GenericArguments[0] != typeParam)
                    goto Failed;
                return SpanTypeIndex.ReadOnlySpan;
        }

    Failed:
        return SpanTypeIndex.None;
    }

    private bool CheckIsRightConstructor(MethodReference method, in MethodSignature signature)
    {
        TypeReference compareType = signature.ParameterTypes[0].Resolve().MakeGenericInstanceType(signature.GenericArguments);
        if (method.DeclaringType.FullName != compareType.FullName)
            return false;

        MethodDefinition? definition = method.Resolve();

        if (definition is null ||
            !definition.HasParameters ||
            definition.IsStatic ||
            !string.Equals(definition.Name, ".ctor"))
            return false;

        Collection<ParameterDefinition> parameters = definition.Parameters;
        if (parameters.Count != 2)
            return false;

        ParameterDefinition param1 = parameters[0];
        if (!param1.ParameterType.IsPointer || param1.ParameterType.GetElementType().FullName != "System.Void")
            return false;

        ParameterDefinition param2 = parameters[1];
        return param2.ParameterType.FullName == "System.Int32";
    }

    private static Instruction? FindPreviousInstruction(Instruction instruction)
    {
        Instruction? previousInstruction = instruction;
        do
        {
            previousInstruction = previousInstruction.Previous;
            if (previousInstruction is null)
                return null;
        } while (IsNopLikeOpCode(previousInstruction.OpCode));
        return previousInstruction;
    }

    private void RemoveRange(ILProcessor processor, Instruction fromInclusive, Instruction toInclusive)
    {
        for (Instruction? current = fromInclusive, next; current is not null && current != toInclusive; current = next)
        {
            next = current.Next;
            processor.Remove(current);
        }
        processor.Remove(toInclusive);
    }

    private void UnwrapSpan(MethodBody body, Instruction instruction, in MethodSignature signature)
    {
        /*
        * current instruction: call SpanUnwrap.Unwrap<T>(ref T)
        * previous instruction we need: newobj Span.ctor<T>(void*, int)
        */
        Instruction? previousInstruction = FindPreviousInstruction(instruction);
        if (previousInstruction is null)
            return;

        ILProcessor? processor = body.GetILProcessor();
        if (previousInstruction.OpCode != OpCodes.Newobj ||
            previousInstruction.Operand is not MethodReference constructor ||
            !CheckIsRightConstructor(constructor, signature))
            goto Fallback;
        Instruction? lengthInstruction = FindPreviousInstruction(previousInstruction);
        if (lengthInstruction is null)
            goto Fallback;

        if (IsSimpleLoadOpCode(lengthInstruction.OpCode))
            RemoveRange(processor, lengthInstruction, instruction);
        else
        {
            RemoveRange(processor, lengthInstruction.Next, instruction);
            processor.InsertAfter(lengthInstruction, Instruction.Create(OpCodes.Pop));
        }

        return;

    Fallback:
        TypeDefinition typeDefinition = signature.ParameterTypes[0].Resolve();
        TypeReference realType = body.Method.Module.ImportReference(
            typeDefinition.MakeGenericInstanceType(signature.GenericArguments),
            signature.GenericArguments[0]);

        ReplaceCalleeToGetPinnableReference(processor, instruction, typeDefinition, realType);
        ReplaceValueLoadToAddressLoad(processor, previousInstruction, realType);

        return;
    }

    private void ReplaceCalleeToGetPinnableReference(ILProcessor processor, Instruction instruction, TypeDefinition typeDefinition, TypeReference realType)
    {
        MethodDefinition method = typeDefinition.Methods.Where(method => method.Name == "GetPinnableReference").First();
        GenericParameter typeParameterT = typeDefinition.GenericParameters[0];
        GenericParameter referenceToT = typeParameterT;
        TypeReference returnType = new ByReferenceType(referenceToT);
        if (method.ReturnType is RequiredModifierType modreq)
        {
            returnType = new RequiredModifierType(
                ModuleDefinition.ImportReference(modreq.ModifierType),
                returnType);
        }

        instruction.Operand = new MethodReference(method.Name, returnType,
            realType)
        {
            HasThis = method.HasThis,
            ExplicitThis = method.ExplicitThis,
            CallingConvention = method.CallingConvention
        };
    }

    private void ReplaceValueLoadToAddressLoad(ILProcessor processor, Instruction instruction, TypeReference typeReference)
    {
        OpCode opCode = instruction.OpCode;

        if (opCode == OpCodes.Ldarg_0)
            ReplaceToLdarga_S_Index(processor, instruction, 0);
        else if (opCode == OpCodes.Ldarg_1)
            ReplaceToLdarga_S_Index(processor, instruction, 1);
        else if (opCode == OpCodes.Ldarg_2)
            ReplaceToLdarga_S_Index(processor, instruction, 2);
        else if (opCode == OpCodes.Ldarg_3)
            ReplaceToLdarga_S_Index(processor, instruction, 3);
        else if (opCode == OpCodes.Ldarg_S)
            ReplaceToLdarga_S(processor, instruction, (ParameterDefinition)instruction.Operand);
        else if (opCode == OpCodes.Ldarg)
            ReplaceToLdarga(processor, instruction, (ParameterDefinition)instruction.Operand);
        else if (opCode == OpCodes.Ldloc_0)
            ReplaceToLdloca_S(processor, instruction, processor.Body.Variables[0]);
        else if (opCode == OpCodes.Ldloc_1)
            ReplaceToLdloca_S(processor, instruction, processor.Body.Variables[1]);
        else if (opCode == OpCodes.Ldloc_2)
            ReplaceToLdloca_S(processor, instruction, processor.Body.Variables[2]);
        else if (opCode == OpCodes.Ldloc_3)
            ReplaceToLdloca_S(processor, instruction, processor.Body.Variables[3]);
        else if (opCode == OpCodes.Ldloc_S)
            ReplaceToLdloca_S(processor, instruction, (VariableDefinition)instruction.Operand);
        else if (opCode == OpCodes.Ldloc)
            ReplaceToLdloca(processor, instruction, (VariableDefinition)instruction.Operand);
        else if (opCode == OpCodes.Ldfld)
            processor.Replace(instruction, Instruction.Create(OpCodes.Ldflda, (FieldReference)instruction.Operand));
        else if (opCode == OpCodes.Ldsfld)
            processor.Replace(instruction, Instruction.Create(OpCodes.Ldsflda, (FieldReference)instruction.Operand));
        else if (opCode == OpCodes.Ldelem_Any)
            processor.Replace(instruction, Instruction.Create(OpCodes.Ldelema, (TypeReference)instruction.Operand));
        else if (opCode == OpCodes.Ldelem_I)
            processor.Replace(instruction, Instruction.Create(OpCodes.Ldelema, processor.Body.Method.Module.TypeSystem.IntPtr));
        else if (opCode == OpCodes.Ldelem_I1)
            processor.Replace(instruction, Instruction.Create(OpCodes.Ldelema, processor.Body.Method.Module.TypeSystem.SByte));
        else if (opCode == OpCodes.Ldelem_I2)
            processor.Replace(instruction, Instruction.Create(OpCodes.Ldelema, processor.Body.Method.Module.TypeSystem.Int16));
        else if (opCode == OpCodes.Ldelem_I4)
            processor.Replace(instruction, Instruction.Create(OpCodes.Ldelema, processor.Body.Method.Module.TypeSystem.Int32));
        else if (opCode == OpCodes.Ldelem_I8)
            processor.Replace(instruction, Instruction.Create(OpCodes.Ldelema, processor.Body.Method.Module.TypeSystem.Int64));
        else if (opCode == OpCodes.Ldelem_R4)
            processor.Replace(instruction, Instruction.Create(OpCodes.Ldelema, processor.Body.Method.Module.TypeSystem.Single));
        else if (opCode == OpCodes.Ldelem_R8)
            processor.Replace(instruction, Instruction.Create(OpCodes.Ldelema, processor.Body.Method.Module.TypeSystem.Double));
        else if (opCode == OpCodes.Ldelem_U1)
            processor.Replace(instruction, Instruction.Create(OpCodes.Ldelema, processor.Body.Method.Module.TypeSystem.Byte));
        else if (opCode == OpCodes.Ldelem_U2)
            processor.Replace(instruction, Instruction.Create(OpCodes.Ldelema, processor.Body.Method.Module.TypeSystem.UInt16));
        else if (opCode == OpCodes.Ldelem_U4)
            processor.Replace(instruction, Instruction.Create(OpCodes.Ldelema, processor.Body.Method.Module.TypeSystem.UInt32));
        else if (CheckIsLoadIndirectOpCode(opCode) || opCode == OpCodes.Ldobj)
            processor.Remove(instruction);
        else
        {
            VariableDefinition tempVariable = new VariableDefinition(typeReference);
            Collection<VariableDefinition> variables = processor.Body.Variables;
            int index = variables.Count;
            variables.Add(tempVariable);
            Instruction storeInstruction, loadAddressInstruction;
            if (index > 255)
            {
                storeInstruction = Instruction.Create(OpCodes.Stloc, tempVariable);
                loadAddressInstruction = Instruction.Create(OpCodes.Ldloca, tempVariable);
            }
            else
            {
                storeInstruction = Instruction.Create(OpCodes.Stloc_S, tempVariable);
                loadAddressInstruction = Instruction.Create(OpCodes.Ldloca_S, tempVariable);
            }
            processor.InsertAfter(instruction, storeInstruction);
            processor.InsertAfter(storeInstruction, loadAddressInstruction);
        }

        static void ReplaceToLdarga_S_Index(ILProcessor processor, Instruction instruction, byte rawIndex)
        {
            if (rawIndex > 3)
                throw new InvalidOperationException();
            MethodBody body = processor.Body;
            MethodReference method = body.Method;
            if (method.HasThis)
                rawIndex--;
            if (rawIndex == byte.MaxValue)
                processor.Replace(instruction, Instruction.Create(OpCodes.Ldarga_S, body.ThisParameter));
            else
                processor.Replace(instruction, Instruction.Create(OpCodes.Ldarga_S, method.Parameters[rawIndex]));
        }

        static void ReplaceToLdarga_S(ILProcessor processor, Instruction instruction, ParameterDefinition definition)
            => processor.Replace(instruction, Instruction.Create(OpCodes.Ldarga_S, definition));

        static void ReplaceToLdarga(ILProcessor processor, Instruction instruction, ParameterDefinition definition)
            => processor.Replace(instruction, Instruction.Create(OpCodes.Ldarga, definition));

        static void ReplaceToLdloca_S(ILProcessor processor, Instruction instruction, VariableDefinition definition)
            => processor.Replace(instruction, Instruction.Create(OpCodes.Ldloca_S, definition));

        static void ReplaceToLdloca(ILProcessor processor, Instruction instruction, VariableDefinition definition)
            => processor.Replace(instruction, Instruction.Create(OpCodes.Ldloca, definition));

        static bool CheckIsLoadIndirectOpCode(in OpCode opCode)
            => opCode == OpCodes.Ldind_I ||
            opCode == OpCodes.Ldind_I1 || opCode == OpCodes.Ldind_I2 || opCode == OpCodes.Ldind_I4 || opCode == OpCodes.Ldind_I8 ||
            opCode == OpCodes.Ldind_U1 || opCode == OpCodes.Ldind_U2 || opCode == OpCodes.Ldind_U4 ||
            opCode == OpCodes.Ldind_R4 || opCode == OpCodes.Ldind_R8;

    }

    private static bool IsNopLikeOpCode(in OpCode opCode)
        =>
        // nop
        opCode == OpCodes.Nop ||
        // conv
        opCode == OpCodes.Conv_I || opCode == OpCodes.Conv_I1 || opCode == OpCodes.Conv_I2 || opCode == OpCodes.Conv_I4 || opCode == OpCodes.Conv_I8 ||
        opCode == OpCodes.Conv_U || opCode == OpCodes.Conv_U1 || opCode == OpCodes.Conv_U2 || opCode == OpCodes.Conv_U4 || opCode == OpCodes.Conv_U8 ||
        opCode == OpCodes.Conv_R_Un || opCode == OpCodes.Conv_R4 || opCode == OpCodes.Conv_R8
        ;

    private static bool IsSimpleLoadOpCode(in OpCode opCode)
        // the simple load opcodes is the operations that pushes a value into the stack, doesn't pops any value out of the stack, and no any side effect or error.
        =>
        // ldc.i4
        opCode == OpCodes.Ldc_I4 || opCode == OpCodes.Ldc_I4_0 || opCode == OpCodes.Ldc_I4_1 || opCode == OpCodes.Ldc_I4_2 || opCode == OpCodes.Ldc_I4_3 ||
        opCode == OpCodes.Ldc_I4_4 || opCode == OpCodes.Ldc_I4_5 || opCode == OpCodes.Ldc_I4_6 || opCode == OpCodes.Ldc_I4_7 || opCode == OpCodes.Ldc_I4_8 ||
        opCode == OpCodes.Ldc_I4_M1 || opCode == OpCodes.Ldc_I4_S ||
        // ldc.i8, ldc.r4, ldc.r8
        opCode == OpCodes.Ldc_I8 || opCode == OpCodes.Ldc_R4 || opCode == OpCodes.Ldc_R8 ||
        // ldarg
        opCode == OpCodes.Ldarg || opCode == OpCodes.Ldarg_0 || opCode == OpCodes.Ldarg_1 || opCode == OpCodes.Ldarg_2 || opCode == OpCodes.Ldarg_3 ||
        opCode == OpCodes.Ldarg_S ||
        // ldloc
        opCode == OpCodes.Ldloc || opCode == OpCodes.Ldloc_0 || opCode == OpCodes.Ldloc_1 || opCode == OpCodes.Ldloc_2 || opCode == OpCodes.Ldloc_3 ||
        opCode == OpCodes.Ldloc_S ||
        // ldarga
        opCode == OpCodes.Ldarga || opCode == OpCodes.Ldarga_S ||
        // ldloca
        opCode == OpCodes.Ldloca || opCode == OpCodes.Ldloca_S ||
        // ldsfld, ldsflda
        opCode == OpCodes.Ldsfld || opCode == OpCodes.Ldsflda
        ;
}