using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Linq;

// TODO: Add safety and code completeness checks

namespace Visitor.Fody
{
    public partial class ModuleWeaver
    {
        public Action<string> LogInfo { get; set; }
        public Action<string> LogWarning { get; set; }
        public Action<string> LogError { get; set; }
        public ModuleDefinition ModuleDefinition { get; set; }

        private MethodReference NotImplementedExceptionRef { get; set; }
        private TypeReference GenericActionRef { get; set; }
        private TypeDefinition GenericActionDefinition { get; set; }


        public ModuleWeaver()
        {
            LogInfo = s => { };
            LogWarning = s => { };
            LogError = s => { };
        }

        public void Execute()
        {
            NotImplementedExceptionRef = ModuleDefinition.ImportReference(typeof(NotImplementedException).GetConstructor(new Type[0]));
            GenericActionRef = ModuleDefinition.ImportReference(typeof(Action<>)); 
            GenericActionDefinition = GenericActionRef.Resolve();

            AddAcceptMethods();

            ProcessAssembly(ModuleDefinition.GetTypes());

            CleanAttributes();
            CleanReferences();
        }

        private static bool IsAnonymousType(TypeDefinition type)
        {
            return type.HasGenericParameters
                && type.Name.Contains("AnonymousType")
                && (type.Name.StartsWith("<>") || type.Name.StartsWith("VB$"))
                && (type.Attributes & TypeAttributes.NotPublic) == TypeAttributes.NotPublic
                && type.CustomAttributes.Where(x => x.AttributeType.FullName == typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute).FullName).Any();
        }


        private void ProcessAssembly(IEnumerable<TypeDefinition> types)
        {
            foreach (var type in types)
            {
                foreach (var method in type.MethodsWithBody())
                {
                    ReplaceCalls(method.Body);
                }

                foreach (var property in type.ConcreteProperties())
                {
                    if (property.GetMethod != null)
                        ReplaceCalls(property.GetMethod.Body);
                    if (property.SetMethod != null)
                        ReplaceCalls(property.SetMethod.Body);
                }
            }
        }

        // TODO: Clean up this massive mess of a method
        private void ReplaceCalls(MethodBody body)
        {
            body.SimplifyMacros();

            var calls = body.Instructions.Where(i => i.OpCode == OpCodes.Call);
            var toDelete = new List<Instruction>();

            foreach (var call in calls)
            {
                if (!(call.Operand is GenericInstanceMethod))
                    continue;

                if (!(call.Previous.OpCode == OpCodes.Ldc_I4))
                    throw new WeavingException("Create's last parameter must be a boolean constant.");

                var originalMethodReference = (GenericInstanceMethod)call.Operand;
                var originalMethodDefinition = originalMethodReference.Resolve();
                var declaringTypeReference = originalMethodReference.DeclaringType;
                var declaringTypeDefinition = declaringTypeReference.Resolve();

                if (originalMethodReference.Name != "Create" || !declaringTypeReference.FullName.StartsWith("Visitor.VisitorFactory`") || !originalMethodDefinition.ContainsGenericParameter || !declaringTypeReference.IsGenericInstance)
                {
                    //LogInfo($"Skipping call {originalMethodReference.Name} on class {declaringTypeReference.FullName}");
                    continue;
                }

                var actionOnMissing = (ActionOnMissing)call.Previous.Operand;

                var visitorTypeReference = originalMethodReference.GenericArguments.First();
                var visitorTypeDefintion = visitorTypeReference.Resolve();
                var declaringGenericType = (GenericInstanceType)declaringTypeReference;
                var interfaceTypeReference = declaringGenericType.GenericArguments.First();
                var interfaceTypeDefinition = interfaceTypeReference.Resolve();

                if (visitorTypeDefintion.Module != ModuleDefinition)
                {
                    LogWarning($"Create references referenced assembly {visitorTypeDefintion.Module.Name}");
                }

                if (visitorTypeDefintion.Interfaces.Where(x => x.InterfaceType.FullName == interfaceTypeReference.FullName).Any())
                {
                    LogWarning(string.Format("{0} already implements {1}, skipping implementation.", visitorTypeDefintion.FullName, interfaceTypeReference.FullName));
                }
                else
                {
                    LogInfo($"Replacing call {declaringTypeReference.Namespace}.{declaringTypeReference.Name}<{interfaceTypeReference.FullName}>::{originalMethodReference.Name}<{visitorTypeReference.FullName}> ({actionOnMissing} on missing)");

                    visitorTypeDefintion.Interfaces.Add(new InterfaceImplementation(ModuleDefinition.ImportReference(interfaceTypeReference)));

                    var impls = new Dictionary<string, Instruction[]>();
                    var implsBy = new Dictionary<string, string>();

                    // TODO: Limit methods to those named Accept? Compile error on non-matching method?
                    var interfaceMethodDefinitions = interfaceTypeDefinition.Methods.Where(x =>
                        x.Parameters.Count == 1
                        && x.Parameters.First().ParameterType.Resolve().Methods.Where(y =>
                            y.Parameters.Count == 1
                            && y.Parameters.First().ParameterType.FullName == interfaceTypeReference.FullName
                        ).Any()
                    ).ToList();

                    if (IsAnonymousType(visitorTypeDefintion))
                    {
                        LogInfo($"\t{visitorTypeDefintion.Name} is anonymous.");

                        var genericTypeRef = originalMethodReference.GenericArguments.First() as GenericInstanceType;
                        if (genericTypeRef == null)
                        {
                            throw new WeavingException("Anonymous type unexpectedly not GenericInstanceType!");
                        }

                        //LogInfo($"\tIs gen instance: {string.Join("\t", genericTypeRef.GenericArguments.Where(x => x is GenericInstanceType).Cast<GenericInstanceType>().Select(x => x.GenericArguments[0]))}");

                        TypeDefinition anonMethodDef = genericTypeRef.Resolve();
                        if (genericTypeRef.GenericArguments.Count != anonMethodDef.Properties.Count)
                        {
                            throw new WeavingException("Generic argument and property count unexpected mismatch!");
                        }

                        for (int x = 0; x < genericTypeRef.GenericArguments.Count; x++)
                        {
                            if (genericTypeRef.GenericArguments[x] is GenericInstanceType propTypeRef)
                            {
                                var prop = anonMethodDef.Properties[x];
                                var propTypeDef = propTypeRef.Resolve();

                                if (propTypeDef != GenericActionDefinition)
                                    continue;

                                var parameterType = ModuleDefinition.ImportReference(propTypeRef.GenericArguments.First());
                                var parameterTypeName = parameterType.FullName;

                                //LogInfo($"{string.Join(", ", genericTypeRef.GenericArguments.Select(a => a.FullName))}");

                                //LogInfo(string.Format("\t{0} => {1}", prop.PropertyType.Name, propTypeRef.FullName));

                                //var genericTypedActionRef = GenericActionDefinition.MakeGenericInstanceType(parameterType);

                                var invokeMethodDef = GenericActionDefinition.Methods.Where(m => m.Name == "Invoke").First();
                                var invokeMethodRef = ModuleDefinition.ImportReference(invokeMethodDef).MakeGeneric(parameterType);

                                if (!impls.ContainsKey(parameterTypeName))
                                {
                                    var labelNotNull = Instruction.Create(OpCodes.Nop);
                                    var opRet = Instruction.Create(OpCodes.Ret);

                                    impls.Add(parameterTypeName, new Instruction[] {
                                        Instruction.Create(OpCodes.Ldarg_0),
                                        Instruction.Create(OpCodes.Call, ModuleDefinition.ImportReference(prop.GetMethod.MakeGeneric(genericTypeRef.GenericArguments.ToArray()))),
                                        Instruction.Create(OpCodes.Dup),
                                        Instruction.Create(OpCodes.Brtrue, labelNotNull),
                                        Instruction.Create(OpCodes.Pop),
                                        Instruction.Create(OpCodes.Br, opRet),
                                        labelNotNull,
                                        Instruction.Create(OpCodes.Ldarg_1),
                                        Instruction.Create(OpCodes.Callvirt, ModuleDefinition.ImportReference(invokeMethodRef)),
                                        opRet
                                    });
                                    implsBy.Add(parameterTypeName, $"{visitorTypeDefintion.Name}.{prop.Name}");
                                }
                            }
                        }
                    }
                    else
                    {
                        // TODO: Further limit what methods qualify? Match the interface name? Must be named Visit?
                        var visitorMethods = visitorTypeDefintion.Methods.Where(x =>
                            x.Parameters.Count == 1
                            && !x.Name.Contains(".")
                            && x.Parameters[0].ParameterType.Resolve().Methods.Where(m =>
                                m.Parameters.Count == 1
                                && m.Parameters[0].ParameterType.FullName == interfaceTypeReference.FullName
                            ).Any()
                        );

                        foreach (var method in visitorMethods)
                        {
                            //LogInfo($"\t{visitorTypeDefintion.Name}.{method.Name}({string.Join(", ", method.Parameters.Select(x => x.ParameterType.Name))})");

                            var parameterType = ModuleDefinition.ImportReference(method.Parameters.First().ParameterType);
                            var parameterTypeName = parameterType.FullName;

                            if (!impls.ContainsKey(parameterTypeName))
                            {
                                impls.Add(parameterTypeName, new Instruction[] {
                                    Instruction.Create(OpCodes.Ldarg_0),
                                    Instruction.Create(OpCodes.Ldarg_1),
                                    Instruction.Create(OpCodes.Call, method),
                                    Instruction.Create(OpCodes.Ret)
                                });
                                implsBy.Add(parameterTypeName, $"{visitorTypeDefintion.Name}.{method.Name}({string.Join(", ", method.Parameters.Select(x => x.ParameterType.Name))})");
                            }
                        }


                        foreach (var prop in visitorTypeDefintion.Properties)
                        {
                            if (prop.PropertyType is GenericInstanceType propTypeRef)
                            {
                                var propTypeDef = propTypeRef.Resolve();

                                if (propTypeDef != GenericActionDefinition)
                                    continue;

                                var parameterType = ModuleDefinition.ImportReference(propTypeRef.GenericArguments.First());
                                var parameterTypeName = parameterType.FullName;

                                //LogInfo($"\t{visitorTypeDefintion.Name}.{prop.Name} => {prop.PropertyType.FullName}");

                                var invokeMethodDef = propTypeDef.Methods.Where(x => x.Name == "Invoke").First();
                                var invokeMethodRef = ModuleDefinition.ImportReference(invokeMethodDef).MakeGeneric(propTypeRef.GenericArguments.ToArray());

                                if (!impls.ContainsKey(parameterTypeName))
                                {
                                    var labelNotNull = Instruction.Create(OpCodes.Nop);
                                    var opRet = Instruction.Create(OpCodes.Ret);

                                    impls.Add(parameterTypeName, new Instruction[] {
                                        Instruction.Create(OpCodes.Ldarg_0),
                                        Instruction.Create(OpCodes.Call, ModuleDefinition.ImportReference(prop.GetMethod)),
                                        Instruction.Create(OpCodes.Dup),
                                        Instruction.Create(OpCodes.Brtrue, labelNotNull),
                                        Instruction.Create(OpCodes.Pop),
                                        Instruction.Create(OpCodes.Br, opRet),
                                        labelNotNull,
                                        Instruction.Create(OpCodes.Ldarg_1),
                                        Instruction.Create(OpCodes.Callvirt, ModuleDefinition.ImportReference(invokeMethodRef)),
                                        opRet
                                    });
                                    implsBy.Add(parameterTypeName, $"{visitorTypeDefintion.Name}.{prop.Name} => {prop.PropertyType.FullName}");
                                }
                            }
                        }
                    }

                    LogInfo($"Found {impls.Count} implementations");
                    foreach (var interfaceMethod in interfaceMethodDefinitions)
                    {
                        var methodName = interfaceTypeDefinition.FullName + "." + interfaceMethod.Name;

                        LogInfo($"\t{interfaceTypeDefinition.Name}::{interfaceMethod.Name}({string.Join(", ", interfaceMethod.Parameters.Select(x => x.ParameterType.Name))}) => {visitorTypeDefintion.FullName}::{methodName}");

                        var impl = new MethodDefinition(methodName,
                            MethodAttributes.Private
                                | MethodAttributes.Final
                                | MethodAttributes.Virtual
                                | MethodAttributes.HideBySig
                                | MethodAttributes.VtableLayoutMask,
                            ModuleDefinition.TypeSystem.Void
                        );

                        var oldParameter = interfaceMethod.Parameters.First();

                        impl.Parameters.Add(new ParameterDefinition(oldParameter.Name, oldParameter.Attributes, ModuleDefinition.ImportReference(oldParameter.ParameterType)));
                        impl.Overrides.Add(ModuleDefinition.ImportReference(interfaceMethod));

                        var parameterTypeName = ModuleDefinition.ImportReference(interfaceMethod.Parameters.First().ParameterType).FullName;

                        if (impls.ContainsKey(parameterTypeName))
                        {
                            impl.Body.Instructions.Append(impls[parameterTypeName]);

                            if (implsBy.ContainsKey(parameterTypeName))
                            {
                                LogInfo($"\t\t\\-> {implsBy[parameterTypeName]}");
                            }
                        }
                        else
                        {
                            switch (actionOnMissing)
                            {
                                case ActionOnMissing.CompileError:
                                    throw new WeavingException($"No implemenation found for {interfaceTypeDefinition.Name}::{interfaceMethod.Name}({string.Join(", ", interfaceMethod.Parameters.Select(x => x.ParameterType.Name))}) in {visitorTypeDefintion.FullName}");
                                case ActionOnMissing.ThrowException:
                                    LogInfo($"\t\tthrow NotImplementedException");
                                    impl.Body.Instructions.Append(
                                        Instruction.Create(OpCodes.Newobj, NotImplementedExceptionRef),
                                        Instruction.Create(OpCodes.Throw)
                                    );
                                    break;
                                case ActionOnMissing.NoOp:
                                    LogInfo($"\t\tno-op");
                                    impl.Body.Instructions.Append(
                                        Instruction.Create(OpCodes.Ret)
                                    );
                                    break;
                            }
                        }
                        visitorTypeDefintion.Methods.Add(impl);
                    }
                }

                toDelete.Add(call.Previous);
                toDelete.Add(call);

                
            }

            foreach(var instruction in toDelete)
            {
                body.Instructions.Remove(instruction);
            }

            body.InitLocals = true;
            body.OptimizeMacros();
        }
    }
}
