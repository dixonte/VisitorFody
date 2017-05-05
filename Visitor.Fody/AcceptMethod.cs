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
        public void AddAcceptMethods()
        {
            var taggedClasses = ModuleDefinition
                .GetTypes()
                .Where(x => x.IsClass && x.BaseType != null)
                .Where(x => HasAcceptsVisitorAttribute(x))
                .ToList();

            foreach (var taggedClass in taggedClasses)
            {
                var acceptsVisitorAttribute = taggedClass.CustomAttributes.GetAttribute(typeof(AcceptsVisitorAttribute).FullName);
                var interfaceProp = acceptsVisitorAttribute.ConstructorArguments.First();

                if (interfaceProp.Value == null)
                {
                    throw new WeavingException($"VisitorInterface property of AcceptsVisitor attribute on {taggedClass.FullName} is null.");
                }

                var interfaceType = interfaceProp.Value as TypeDefinition;
                var interfaceReference = ModuleDefinition.ImportReference(interfaceType);
                var classReference = ModuleDefinition.ImportReference(taggedClass);

                var methodDef = interfaceType.Methods.Where(x => x.IsPublic && x.Name == "Visit" && x.Parameters.Count == 1 && x.Parameters[0].ParameterType == classReference).FirstOrDefault();

                if (methodDef == null)
                {
                    throw new WeavingException($"No suitable Visit method found in {interfaceType.FullName} for {classReference.FullName}");
                }

                var methodReference = ModuleDefinition.ImportReference(methodDef);

                if (!HasAcceptMethod(taggedClass, interfaceReference))
                {
                    LogInfo($"Adding Accept({interfaceType.Name}) to {classReference.FullName}");
                    taggedClass.Methods.Add(CreateAcceptMethod("Accept", methodReference, interfaceReference));
                }
            }
        }

        MethodDefinition CreateAcceptMethod(string methodName, MethodReference methodReference, TypeReference interfaceReference)
        {
            const MethodAttributes Attributes = MethodAttributes.Public |
                                                MethodAttributes.HideBySig |
                                                /*MethodAttributes.Final |
                                                MethodAttributes.SpecialName |
                                                MethodAttributes.NewSlot |*/
                                                MethodAttributes.Virtual;

            var method = new MethodDefinition(methodName, Attributes, ModuleDefinition.TypeSystem.Void);
            method.Parameters.Add(new ParameterDefinition("visitor", ParameterAttributes.None, interfaceReference));

            method.Body.Instructions.Append(
                Instruction.Create(OpCodes.Ldarg_1),
                Instruction.Create(OpCodes.Ldarg_0),
                Instruction.Create(OpCodes.Callvirt, methodReference),
                Instruction.Create(OpCodes.Ret));
            method.Body.InitLocals = true;
            method.Body.OptimizeMacros();

            return method;
        }

        static bool HasAcceptsVisitorAttribute(TypeDefinition typeDefinition)
        {
            return typeDefinition.CustomAttributes.ContainsAttribute(typeof(AcceptsVisitorAttribute).FullName);
        }

        static bool HasAcceptMethod(TypeDefinition typeDefinition, TypeReference interfaceRef)
        {
            return typeDefinition.Methods
                .Where(x => x.IsPublic && x.Name == "Accept" && x.Parameters.Count == 1 && x.Parameters[0].ParameterType == interfaceRef)
                .Any();
        }
    }
}
