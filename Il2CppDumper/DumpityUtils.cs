using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2CppDumper
{
    public class DumpityUtils
    {
        public static MethodDefinition GetConstructor(TypeDefinition def)
        {
            foreach (var m in def.Methods)
            {
                if (m.IsConstructor && !m.HasParameters)
                {
                    return m;
                }
            }
            // Need to add a constructor if no constructor can be found without parameters

            var newConstructor = new MethodDefinition(".ctor", MethodAttributes.Public
                | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, def.Module.TypeSystem.Void);

            //newConstructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            //newConstructor.Body.Instructions.Add(Instruction.Create(OpCodes.Nop));
            newConstructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));

            def.Methods.Add(newConstructor);
            return newConstructor;
        }
        public static bool IsPrimitive(TypeReference r)
        {
            return r.IsPrimitive || r.MetadataType == MetadataType.String;
        }
        public static List<FieldDefinition> FindSerializedData(TypeDefinition def, TypeReference serializeFieldAttr)
        {
            // So if it is a monobehaviour, we need to see what gameobject it is on. 
            // We also need to get all of the inherited fields/properties in the right order to save em.
            var fields = new List<FieldDefinition>();
            foreach (var f in def.Fields)
            {
                //Console.WriteLine($"Trying Field: {f}");
                foreach (var atr in f.CustomAttributes)
                {
                    //Console.WriteLine($"Custom Attribute: {atr.AttributeType.FullName} compared to {SerializeFieldAttr.FullName}");
                    if (atr.AttributeType.FullName.Equals(serializeFieldAttr.FullName))
                    {
                        Console.WriteLine($"{f.Name} has type: {f.FieldType} (from: {f.FieldType.Module.Assembly}) and MetadataType: {f.FieldType.MetadataType}");
                        fields.Add(f);
                    }
                }
            }
            return fields;
        }
        public static bool IsSerializable(TypeDefinition t)
        {
            // It contains attributes, also needs to contain Serailizable
            // Possibly only needs to be within the same assembly to serialize? Not sure yet.
            return t.IsSerializable;
        }
    }
}
