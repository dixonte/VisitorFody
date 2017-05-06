using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            //CleanReferences();
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

        private void ReplaceCalls(MethodBody body)
        {
            body.SimplifyMacros();

            var calls = body.Instructions.Where(i => i.OpCode == OpCodes.Call);

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

                if (visitorTypeDefintion.Interfaces.Where(x => x.InterfaceType == interfaceTypeReference).Any())
                {
                    LogWarning(string.Format("{0} already implements {1}, skipping implementation.", visitorTypeDefintion.FullName, interfaceTypeReference.FullName));
                }
                else
                {
                    LogInfo($"Replacing call {declaringTypeReference.Namespace}.{declaringTypeReference.Name}<{interfaceTypeReference.FullName}>::{originalMethodReference.Name}<{visitorTypeReference.FullName}> ({actionOnMissing} on missing)");

                    visitorTypeDefintion.Interfaces.Add(new InterfaceImplementation(interfaceTypeReference));

                    var impls = new Dictionary<TypeReference, Instruction[]>();
                    var implsBy = new Dictionary<TypeReference, string>();

                    var interfaceMethodDefinitions = interfaceTypeDefinition.Methods.Where(x =>
                        x.Parameters.Count == 1
                        && x.Parameters.First().ParameterType.Resolve().Methods.Where(y =>
                            y.Parameters.Count == 1
                            && y.Parameters.First().ParameterType == interfaceTypeReference
                        ).Any()
                    ).ToList();

                    if (IsAnonymousType(visitorTypeDefintion))
                    {
                        LogInfo($"\t{visitorTypeDefintion.Name} is anonymous.");

                        var param = call.Previous.Previous;
                        if (param.OpCode != OpCodes.Newobj)
                        {
                            throw new WeavingException("Anonymous type parameters must be declared in-situ.");
                        }

                        var anonMethodRef = (MethodReference)param.Operand;
                        var genericTypeRef = anonMethodRef.DeclaringType as GenericInstanceType;
                        if (genericTypeRef == null)
                        {
                            throw new WeavingException("Anonymous type unexpectedly not GenericInstanceType!");
                        }

                        LogInfo($"\tIs gen instance: {string.Join("\t", genericTypeRef.GenericArguments.Where(x => x is GenericInstanceType).Cast<GenericInstanceType>().Select(x => x.GenericArguments[0]))}");

                        TypeDefinition anonMethodDef = genericTypeRef.Resolve();
                        if (genericTypeRef.GenericArguments.Count != anonMethodDef.Properties.Count)
                        {
                            throw new WeavingException("Generic argument and property count unexpected mismatch!");
                        }

                        for (int x = 0; x < genericTypeRef.GenericArguments.Count; x++)
                        {
                            TypeReference genericArg = genericTypeRef.GenericArguments[x];
                            PropertyDefinition prop = anonMethodDef.Properties[x];

                            LogInfo(string.Format("\t{0} => {1}", prop.PropertyType.Name, genericArg.FullName));
                        }
                    }
                    else
                    {
                        var visitorMethods = visitorTypeDefintion.Methods.Where(x =>
                            x.Parameters.Count == 1
                            && !x.Name.Contains(".")
                            && x.Parameters[0].ParameterType.Resolve().Methods.Where(m => 
                                m.Parameters.Count == 1 
                                && m.Parameters[0].ParameterType == interfaceTypeReference
                            ).Any()
                        );

                        foreach (var method in visitorMethods)
                        {
                            //LogInfo($"\t{visitorTypeDefintion.Name}.{method.Name}({string.Join(", ", method.Parameters.Select(x => x.ParameterType.Name))})");

                            var parameterType = method.Parameters.First().ParameterType;

                            if (!impls.ContainsKey(parameterType))
                            {
                                impls.Add(parameterType, new Instruction[] {
                                    Instruction.Create(OpCodes.Ldarg_0),
                                    Instruction.Create(OpCodes.Ldarg_1),
                                    Instruction.Create(OpCodes.Call, method),
                                    Instruction.Create(OpCodes.Ret)
                                });
                                implsBy.Add(parameterType, $"{visitorTypeDefintion.Name}.{method.Name}({string.Join(", ", method.Parameters.Select(x => x.ParameterType.Name))})");
                            }
                        }


                        foreach (var prop in visitorTypeDefintion.Properties)
                        {
                            if (prop.PropertyType is GenericInstanceType propTypeRef)
                            {
                                var propTypeDef = propTypeRef.Resolve();

                                if (propTypeDef != GenericActionDefinition)
                                    continue;

                                var parameterType = propTypeRef.GenericArguments.First();

                                //LogInfo($"\t{visitorTypeDefintion.Name}.{prop.Name} => {prop.PropertyType.FullName}");

                                var invokeMethodDef = propTypeDef.Methods.Where(x => x.Name == "Invoke").First();
                                var invokeMethodRef = ModuleDefinition.ImportReference(invokeMethodDef).MakeGeneric(propTypeRef.GenericArguments.ToArray());

                                if (!impls.ContainsKey(parameterType))
                                {
                                    var labelNotNull = Instruction.Create(OpCodes.Nop);
                                    var opRet = Instruction.Create(OpCodes.Ret);

                                    impls.Add(parameterType, new Instruction[] {
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
                                    implsBy.Add(parameterType, $"{visitorTypeDefintion.Name}.{prop.Name} => {prop.PropertyType.FullName}");
                                }
                            }
                        }
                    }


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
                        impl.Parameters.Add(interfaceMethod.Parameters[0]);
                        impl.Overrides.Add(ModuleDefinition.ImportReference(interfaceMethod));

                        var parameterType = interfaceMethod.Parameters.First().ParameterType;

                        if (impls.ContainsKey(parameterType))
                        {
                            impl.Body.Instructions.Append(impls[parameterType]);

                            if (implsBy.ContainsKey(parameterType))
                            {
                                LogInfo($"\t\t\\-> {implsBy[parameterType]}");
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

                call.Previous.OpCode = OpCodes.Nop;
                call.OpCode = OpCodes.Nop;
            }

            body.InitLocals = true;
            body.OptimizeMacros();
        }
    }
}
