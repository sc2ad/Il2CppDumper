using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Il2CppDumper
{
    public static class EmitHelper
    {
        public static void Ldarg(ILProcessor il, int paramId)
        {
            switch (paramId)
            {
                case 0:
                    il.Emit(OpCodes.Ldarg_0);
                    break;
                case 1:
                    il.Emit(OpCodes.Ldarg_1);
                    break;
                case 2:
                    il.Emit(OpCodes.Ldarg_2);
                    break;
                case 3:
                    il.Emit(OpCodes.Ldarg_3);
                    break;
                default:
                    il.Emit(OpCodes.Ldarg_S, (byte)paramId);
                    break;
            }
        }
        public static void LdcI4(ILProcessor il, int toLoad)
        {
            switch (toLoad)
            {
                case 0:
                    il.Emit(OpCodes.Ldc_I4_0);
                    break;
                case 1:
                    il.Emit(OpCodes.Ldc_I4_1);
                    break;
                case 2:
                    il.Emit(OpCodes.Ldc_I4_2);
                    break;
                case 3:
                    il.Emit(OpCodes.Ldc_I4_3);
                    break;
                case 4:
                    il.Emit(OpCodes.Ldc_I4_4);
                    break;
                case 5:
                    il.Emit(OpCodes.Ldc_I4_5);
                    break;
                case 6:
                    il.Emit(OpCodes.Ldc_I4_6);
                    break;
                case 7:
                    il.Emit(OpCodes.Ldc_I4_7);
                    break;
                case 8:
                    il.Emit(OpCodes.Ldc_I4_8);
                    break;
                default:
                    il.Emit(OpCodes.Ldc_I4, toLoad);
                    break;
            }
        }
        private static PropertyDefinition staticDef;
        public static PropertyDefinition CreateClassGetter(TypeDefinition def, TypeReference netSDKIl2cppClass, MethodReference netSDKGetClass, string className)
        {
            var existing = def.Properties.FirstOrDefault(p => p.Name == "_class" && p.PropertyType.FullName == netSDKIl2cppClass.FullName);
            if (existing != null)
                return existing;
            // Assumes there are no fields called __class and no properties called _class
            var field = new FieldDefinition("__class", FieldAttributes.Static | FieldAttributes.Private, def.Module.ImportReference(netSDKIl2cppClass));
            def.Fields.Add(field);
            var prop = new PropertyDefinition("_class", PropertyAttributes.None, def.Module.ImportReference(netSDKIl2cppClass));
            prop.GetMethod = new MethodDefinition("get_" + prop.Name, MethodAttributes.SpecialName | MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig, def.Module.ImportReference(netSDKIl2cppClass));
            prop.GetMethod.SemanticsAttributes = MethodSemanticsAttributes.Getter;
            prop.GetMethod.DeclaringType = def;
            def.Methods.Add(prop.GetMethod);
            prop.DeclaringType = def;
            prop.HasThis = false;
            var m = prop.GetMethod.Body.GetILProcessor();

            // Set locals
            m.Body.Variables.Add(new VariableDefinition(def.Module.ImportReference(typeof(bool))));
            m.Body.Variables.Add(new VariableDefinition(def.Module.ImportReference(netSDKIl2cppClass)));

            // Create getter method
            m.Emit(OpCodes.Nop);
            m.Emit(OpCodes.Ldsfld, field);
            m.Emit(OpCodes.Ldnull);
            m.Emit(OpCodes.Ceq);
            m.Emit(OpCodes.Stloc_0);
            m.Emit(OpCodes.Ldloc_0);
            // If the field is not null, skip to the ret
            var skipGet = Instruction.Create(OpCodes.Ldsfld, field);
            m.Emit(OpCodes.Brfalse_S, skipGet);
            // Otherwise, set the field to NET_SDK.SDK.GetClass()
            m.Emit(OpCodes.Ldstr, className);
            m.Emit(OpCodes.Call, def.Module.ImportReference(netSDKGetClass));
            m.Emit(OpCodes.Stsfld, field);
            m.Append(skipGet);
            m.Emit(OpCodes.Stloc_1);
            var exit = Instruction.Create(OpCodes.Ldloc_1);
            m.Emit(OpCodes.Br_S, exit);
            m.Append(exit);
            m.Emit(OpCodes.Ret);
            def.Properties.Add(prop);
            staticDef = prop;
            return prop;
        }
        public static PropertyDefinition CreateMethodGetter(TypeDefinition def, TypeReference netSDKIl2cppMethod, MethodReference netSDKGetMethod, MethodDefinition methodDefinition, MethodReference getClassProperty)
        {
            string methodPropertyName = "_" + methodDefinition.Name + "_" + methodDefinition.Parameters.Count;
            // Append a _1 for each duplicate overload (same param count and name)
            while (def.Properties.Any(pd => pd.Name == methodPropertyName))
            {
                methodPropertyName += "_1";
            }
            var field = new FieldDefinition("_" + methodPropertyName, FieldAttributes.Static | FieldAttributes.Private, def.Module.ImportReference(netSDKIl2cppMethod));
            def.Fields.Add(field);
            var prop = new PropertyDefinition(methodPropertyName, PropertyAttributes.None, def.Module.ImportReference(netSDKIl2cppMethod));
            prop.GetMethod = new MethodDefinition("get_" + prop.Name, MethodAttributes.SpecialName | MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.HideBySig, def.Module.ImportReference(netSDKIl2cppMethod));
            prop.GetMethod.SemanticsAttributes = MethodSemanticsAttributes.Getter;
            prop.GetMethod.DeclaringType = def;
            def.Methods.Add(prop.GetMethod);
            prop.DeclaringType = def;
            prop.HasThis = false;
            var m = prop.GetMethod.Body.GetILProcessor();

            // Set locals
            m.Body.Variables.Add(new VariableDefinition(def.Module.ImportReference(typeof(bool))));
            m.Body.Variables.Add(new VariableDefinition(def.Module.ImportReference(netSDKIl2cppMethod)));

            // Create getter method
            m.Emit(OpCodes.Nop);
            m.Emit(OpCodes.Ldsfld, field);
            m.Emit(OpCodes.Ldnull);
            m.Emit(OpCodes.Ceq);
            m.Emit(OpCodes.Stloc_0);
            m.Emit(OpCodes.Ldloc_0);
            // If the field is not null, skip to the ret
            var skipGet = Instruction.Create(OpCodes.Ldsfld, field);
            m.Emit(OpCodes.Brfalse_S, skipGet);
            // Otherwise, set the field to IL2CPP_Class.GetMethod
            m.Emit(OpCodes.Call, getClassProperty);
            m.Emit(OpCodes.Ldstr, methodDefinition.Name);
            LdcI4(m, methodDefinition.Parameters.Count);
            m.Emit(OpCodes.Call, def.Module.ImportReference(netSDKGetMethod));
            m.Emit(OpCodes.Stsfld, field);
            m.Append(skipGet);
            m.Emit(OpCodes.Stloc_1);
            var exit = Instruction.Create(OpCodes.Ldloc_1);
            m.Emit(OpCodes.Br_S, exit);
            m.Append(exit);
            m.Emit(OpCodes.Ret);
            def.Properties.Add(prop);
            return prop;
        }
    }
}
