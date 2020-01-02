using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Collections.Generic;

namespace Il2CppDumper
{
    public class DummyAssemblyGenerator
    {
        public List<AssemblyDefinition> Assemblies = new List<AssemblyDefinition>();

        private Metadata metadata;
        private Il2Cpp il2Cpp;
        private Dictionary<long, TypeDefinition> typeDefinitionDic = new Dictionary<long, TypeDefinition>();
        private Dictionary<long, GenericParameter> genericParameterDic = new Dictionary<long, GenericParameter>();
        private MethodDefinition attributeAttribute;
        private TypeReference stringType;
        private Dictionary<string, MethodDefinition> knownAttributes = new Dictionary<string, MethodDefinition>();

        public DummyAssemblyGenerator(Metadata metadata, Il2Cpp il2Cpp, string netSDKPath = "")
        {
            this.metadata = metadata;
            this.il2Cpp = il2Cpp;

            //Il2CppDummyDll
            var il2CppDummyDll = Il2CppDummyDll.Create();
            Assemblies.Add(il2CppDummyDll);
            var addressAttribute = il2CppDummyDll.MainModule.Types.First(x => x.Name == "AddressAttribute").Methods[0];
            var fieldOffsetAttribute = il2CppDummyDll.MainModule.Types.First(x => x.Name == "FieldOffsetAttribute").Methods[0];
            attributeAttribute = il2CppDummyDll.MainModule.Types.First(x => x.Name == "AttributeAttribute").Methods[0];
            stringType = il2CppDummyDll.MainModule.TypeSystem.String;

            var resolver = new MyAssemblyResolver();
            var moduleParameters = new ModuleParameters
            {
                Kind = ModuleKind.Dll,
                AssemblyResolver = resolver
            };
            resolver.Register(il2CppDummyDll);

            AssemblyDefinition netSDK = null;
            AssemblyDefinition createdNetSDK = null;
            TypeDefinition netSDKIl2cppObject = null;
            TypeDefinition netSDKIl2cppClass = null;
            TypeDefinition netSDKIl2cppMethod = null;
            MethodReference netSDKGetClass = null;
            MethodReference netSDKGetMethod = null;
            MethodReference netSDKInvoke0args = null;
            MethodReference netSDKInvoke = null;
            MethodReference netSDKUnbox = null;
            MethodReference netSDKUnboxString = null;
            if (!string.IsNullOrEmpty(netSDKPath) && File.Exists(netSDKPath))
            {
                netSDK = AssemblyDefinition.ReadAssembly(netSDKPath);
                Assemblies.Add(netSDK);
                resolver.Register(netSDK);
                createdNetSDK = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("GameAssembly", new Version("0.1.0.0")), "GameAssembly.dll", moduleParameters);
                Assemblies.Add(createdNetSDK);
                resolver.Register(createdNetSDK);
                netSDKIl2cppObject = netSDK.MainModule.GetType("NET_SDK.Reflection", "IL2CPP_Object");
                netSDKIl2cppClass = netSDK.MainModule.GetType("NET_SDK.Reflection", "IL2CPP_Class");
                netSDKIl2cppMethod = netSDK.MainModule.GetType("NET_SDK.Reflection", "IL2CPP_Method");
                netSDKGetClass = netSDK.MainModule.GetType("NET_SDK", "SDK").Methods.FirstOrDefault(m => m.Name == "GetClass" && m.Parameters.Count == 1 && m.Parameters[0].Name == "fullname");
                netSDKGetMethod = netSDKIl2cppClass.Methods.FirstOrDefault(m => m.Name == "GetMethod" && m.Parameters.Count == 1);
                netSDKInvoke0args = netSDKIl2cppMethod.Methods.FirstOrDefault(m => m.Name == "Invoke" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == netSDKIl2cppObject.FullName);
                netSDKInvoke = netSDKIl2cppMethod.Methods.FirstOrDefault(m => m.Name == "Invoke" && m.Parameters.Count > 1 && m.Parameters[0].ParameterType.FullName == netSDKIl2cppObject.FullName);
                netSDKUnbox = netSDKIl2cppObject.Methods.FirstOrDefault(m => m.Name == "Unbox");
                netSDKUnboxString = netSDKIl2cppObject.Methods.FirstOrDefault(m => m.Name == "UnboxString");
            }

            var fieldDefinitionDic = new Dictionary<int, FieldDefinition>();
            var methodDefinitionDic = new Dictionary<int, MethodDefinition>();
            var parameterDefinitionDic = new Dictionary<int, ParameterDefinition>();
            var propertyDefinitionDic = new Dictionary<int, PropertyDefinition>();
            var eventDefinitionDic = new Dictionary<int, EventDefinition>();

            //创建程序集，同时创建所有类
            foreach (var imageDef in metadata.imageDefs)
            {
                var imageName = metadata.GetStringFromIndex(imageDef.nameIndex);
                var assemblyName = new AssemblyNameDefinition(imageName.Replace(".dll", ""), new Version("3.7.1.6"));
                var assemblyDefinition = AssemblyDefinition.CreateAssembly(assemblyName, imageName, moduleParameters);
                resolver.Register(assemblyDefinition);
                Assemblies.Add(assemblyDefinition);
                var moduleDefinition = assemblyDefinition.MainModule;
                moduleDefinition.Types.Clear();//清除自动创建的<Module>类
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (var index = imageDef.typeStart; index < typeEnd; ++index)
                {
                    var typeDef = metadata.typeDefs[index];
                    var namespaceName = metadata.GetStringFromIndex(typeDef.namespaceIndex);
                    var typeName = metadata.GetStringFromIndex(typeDef.nameIndex);
                    TypeDefinition typeDefinition;
                    if (typeDef.declaringTypeIndex != -1)//nested types
                    {
                        typeDefinition = typeDefinitionDic[index];
                    }
                    else
                    {
                        typeDefinition = new TypeDefinition(namespaceName, typeName, (TypeAttributes)typeDef.flags);
                        moduleDefinition.Types.Add(typeDefinition);
                        typeDefinitionDic.Add(index, typeDefinition);
                    }
                    //nestedtype
                    for (int i = 0; i < typeDef.nested_type_count; i++)
                    {
                        var nestedIndex = metadata.nestedTypeIndices[typeDef.nestedTypesStart + i];
                        var nestedTypeDef = metadata.typeDefs[nestedIndex];
                        var nestedTypeDefinition = new TypeDefinition(metadata.GetStringFromIndex(nestedTypeDef.namespaceIndex), metadata.GetStringFromIndex(nestedTypeDef.nameIndex), (TypeAttributes)nestedTypeDef.flags);
                        typeDefinition.NestedTypes.Add(nestedTypeDefinition);
                        typeDefinitionDic.Add(nestedIndex, nestedTypeDefinition);
                    }
                }
            }
            //先单独处理，因为不知道会不会有问题
            for (var index = 0; index < metadata.typeDefs.Length; ++index)
            {
                var typeDef = metadata.typeDefs[index];
                var typeDefinition = typeDefinitionDic[index];
                //parent
                if (typeDef.parentIndex >= 0)
                {
                    var parentType = il2Cpp.types[typeDef.parentIndex];
                    var parentTypeRef = GetTypeReference(typeDefinition, parentType);
                    typeDefinition.BaseType = parentTypeRef;
                }
                //interfaces
                for (int i = 0; i < typeDef.interfaces_count; i++)
                {
                    var interfaceType = il2Cpp.types[metadata.interfaceIndices[typeDef.interfacesStart + i]];
                    var interfaceTypeRef = GetTypeReference(typeDefinition, interfaceType);
                    typeDefinition.Interfaces.Add(new InterfaceImplementation(interfaceTypeRef));
                }
            }
            //处理field, method, property等等
            for (var imageIndex = 0; imageIndex < metadata.imageDefs.Length; imageIndex++)
            {
                var imageDef = metadata.imageDefs[imageIndex];
                var typeEnd = imageDef.typeStart + imageDef.typeCount;
                for (int index = imageDef.typeStart; index < typeEnd; index++)
                {
                    var typeDef = metadata.typeDefs[index];
                    var typeDefinition = typeDefinitionDic[index];

                    bool isStruct = false;
                    if (typeDef.parentIndex >= 0)
                    {
                        var parent = il2Cpp.types[typeDef.parentIndex];
                        isStruct = parent?.type == Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE;
                    }
                    TypeDefinition netSDKDefinition = null;
                    if (netSDK != null)
                    {
                        netSDKDefinition = new TypeDefinition(typeDefinition.Namespace, typeDefinition.Name, (TypeAttributes)typeDef.flags);
                        createdNetSDK.MainModule.Types.Add(netSDKDefinition);
                    }

                    //field
                    var fieldEnd = typeDef.fieldStart + typeDef.field_count;
                    for (var i = typeDef.fieldStart; i < fieldEnd; ++i)
                    {
                        var fieldDef = metadata.fieldDefs[i];
                        var fieldType = il2Cpp.types[fieldDef.typeIndex];
                        var fieldName = metadata.GetStringFromIndex(fieldDef.nameIndex);
                        var fieldTypeRef = GetTypeReference(typeDefinition, fieldType);
                        var fieldDefinition = new FieldDefinition(fieldName, (FieldAttributes)fieldType.attrs, fieldTypeRef);
                        typeDefinition.Fields.Add(fieldDefinition);
                        fieldDefinitionDic.Add(i, fieldDefinition);
                        //fieldDefault
                        if (fieldDefinition.HasDefault)
                        {
                            var fieldDefault = metadata.GetFieldDefaultValueFromIndex(i);
                            if (fieldDefault != null && fieldDefault.dataIndex != -1)
                            {
                                fieldDefinition.Constant = GetDefaultValue(fieldDefault.dataIndex, fieldDefault.typeIndex);
                            }
                        }
                        //fieldOffset
                        var fieldOffset = il2Cpp.GetFieldOffsetFromIndex(index, i - typeDef.fieldStart, i, isStruct);
                        if (fieldOffset >= 0)
                        {
                            var customAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(fieldOffsetAttribute));
                            var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{fieldOffset:X}"));
                            customAttribute.Fields.Add(offset);
                            fieldDefinition.CustomAttributes.Add(customAttribute);
                        }
                    }
                    //method
                    var methodEnd = typeDef.methodStart + typeDef.method_count;
                    for (var i = typeDef.methodStart; i < methodEnd; ++i)
                    {
                        var methodDef = metadata.methodDefs[i];
                        var methodName = metadata.GetStringFromIndex(methodDef.nameIndex);
                        var methodDefinition = new MethodDefinition(methodName, (MethodAttributes)methodDef.flags, typeDefinition.Module.ImportReference(typeof(void)));
                        methodDefinition.ImplAttributes = (MethodImplAttributes)methodDef.iflags;
                        typeDefinition.Methods.Add(methodDefinition);
                        var methodReturnType = il2Cpp.types[methodDef.returnType];
                        methodDefinition.ReturnType = GetTypeReferenceWithByRef(methodDefinition, methodReturnType);
                        if (methodDefinition.HasBody && typeDefinition.BaseType?.FullName != "System.MulticastDelegate")
                        {
                            var ilprocessor = methodDefinition.Body.GetILProcessor();
                            ilprocessor.Append(ilprocessor.Create(OpCodes.Nop));
                        }
                        methodDefinitionDic.Add(i, methodDefinition);
                        //method parameter
                        for (var j = 0; j < methodDef.parameterCount; ++j)
                        {
                            var parameterDef = metadata.parameterDefs[methodDef.parameterStart + j];
                            var parameterName = metadata.GetStringFromIndex(parameterDef.nameIndex);
                            var parameterType = il2Cpp.types[parameterDef.typeIndex];
                            var parameterTypeRef = GetTypeReferenceWithByRef(methodDefinition, parameterType);
                            var parameterDefinition = new ParameterDefinition(parameterName, (ParameterAttributes)parameterType.attrs, parameterTypeRef);
                            methodDefinition.Parameters.Add(parameterDefinition);
                            parameterDefinitionDic.Add(methodDef.parameterStart + j, parameterDefinition);
                            //ParameterDefault
                            if (parameterDefinition.HasDefault)
                            {
                                var parameterDefault = metadata.GetParameterDefaultValueFromIndex(methodDef.parameterStart + j);
                                if (parameterDefault != null && parameterDefault.dataIndex != -1)
                                {
                                    parameterDefinition.Constant = GetDefaultValue(parameterDefault.dataIndex, parameterDefault.typeIndex);
                                }
                            }
                        }
                        //补充泛型参数
                        if (methodDef.genericContainerIndex >= 0)
                        {
                            var genericContainer = metadata.genericContainers[methodDef.genericContainerIndex];
                            if (genericContainer.type_argc > methodDefinition.GenericParameters.Count)
                            {
                                for (int j = 0; j < genericContainer.type_argc; j++)
                                {
                                    var genericParameterIndex = genericContainer.genericParameterStart + j;
                                    if (!genericParameterDic.TryGetValue(genericParameterIndex, out var genericParameter))
                                    {
                                        CreateGenericParameter(genericParameterIndex, methodDefinition);
                                    }
                                    else
                                    {
                                        if (!methodDefinition.GenericParameters.Contains(genericParameter))
                                        {
                                            methodDefinition.GenericParameters.Add(genericParameter);
                                        }
                                    }
                                }
                            }
                        }
                        //methodAddress
                        var methodPointer = il2Cpp.GetMethodPointer(methodDef.methodIndex, i, imageIndex, methodDef.token);
                        if (methodPointer > 0)
                        {
                            var customAttribute = new CustomAttribute(typeDefinition.Module.ImportReference(addressAttribute));
                            var fixedMethodPointer = il2Cpp.FixPointer(methodPointer);
                            var rva = new CustomAttributeNamedArgument("RVA", new CustomAttributeArgument(stringType, $"0x{fixedMethodPointer:X}"));
                            var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{il2Cpp.MapVATR(methodPointer):X}"));
                            customAttribute.Fields.Add(rva);
                            customAttribute.Fields.Add(offset);
                            methodDefinition.CustomAttributes.Add(customAttribute);
                        }
                        if (netSDK != null)
                        {
                            // If we have NET SDK available, write the IL on the inside of this method to use NET_SDK
                            // I'm not sure how much we would need to wrap, or if these DLLs will even be referencable,
                            // but it's better than nothing, I suppose
                            // Overall goal is to convert the parameters we have for the original function to IntPtrs,
                            // or possibly IL2CPP_Objects and use those in the invoke call.

                            // First we need to check our MethodDefinition's return type and parameter types/names to create the signature
                            var netMethod = new MethodDefinition(methodDefinition.Name, (MethodAttributes)methodDef.flags | MethodAttributes.Static, netSDKDefinition.Module.ImportReference(typeof(void)));
                            if (!netMethod.HasBody || typeDefinition.BaseType?.FullName == "System.MulticastDelegate")
                            {
                                goto end;
                            }
                            if (methodDefinition.IsConstructor)
                                goto end;
                            bool isPrimitive = methodDefinition.ReturnType.IsPrimitive;
                            // TODO: Value types
                            bool isStringReturn = methodDefinition.ReturnType.Equals(stringType);
                            // Correct return type
                            if (isPrimitive || isStringReturn)
                                // Set it to a primitive, since we will unbox it to one here
                                netMethod.ReturnType = methodDefinition.ReturnType;
                            else
                                // Set it to an IL2CPP_Object
                                netMethod.ReturnType = createdNetSDK.MainModule.ImportReference(netSDKIl2cppObject);
                            // Correct parameter types
                            foreach (var param in methodDefinition.Parameters)
                            {
                                if (param.ParameterType != null)
                                {
                                    ParameterDefinition def = null;
                                    if (param.ParameterType.Equals(stringType))
                                        def = new ParameterDefinition(param.Name, param.Attributes, param.ParameterType);
                                    else if (param.ParameterType.IsPrimitive)
                                        def = new ParameterDefinition(param.Name, param.Attributes, param.ParameterType);
                                    else
                                        def = new ParameterDefinition(param.Name, param.Attributes, netSDKDefinition.Module.ImportReference(netSDKIl2cppObject));
                                    netMethod.Parameters.Add(def);
                                }
                            }
                            // If this method is an instance method, we need to also insert a 0th parameter of type IL2CPP_Object
                            // This is for the 'self' parameter
                            // Right now, just assume that no parameter will be called _self
                            if (!methodDefinition.IsStatic)
                                netMethod.Parameters.Insert(0, new ParameterDefinition("_self", ParameterAttributes.None, createdNetSDK.MainModule.ImportReference(netSDKIl2cppObject)));
                            // We also need to make the method static (always)
                            netMethod.IsStatic = true;

                            // Initialize locals
                            netMethod.Body.Variables.Add(new VariableDefinition(netSDKDefinition.Module.ImportReference(typeof(bool))));
                            netMethod.Body.Variables.Add(new VariableDefinition(netSDKDefinition.Module.ImportReference(typeof(bool))));
                            netMethod.Body.Variables.Add(new VariableDefinition(netSDKDefinition.Module.ImportReference(netMethod.ReturnType)));

                            // Now we need to add the fields (if needed) for the IL2CPP_Class and IL2CPP_Method
                            // Attempt to make or get the existing class getter
                            var getClassProperty = EmitHelper.CreateClassGetter(netSDKDefinition, netSDKIl2cppClass, netSDKGetClass, typeDefinition.FullName);
                            // We always have to make a new method field
                            var getMethodProperty = EmitHelper.CreateMethodGetter(netSDKDefinition, netSDKIl2cppMethod, netSDKGetMethod, methodDefinition, getClassProperty.GetMethod);
                            
                            // Now we need to add the IL for getting the method (handled within CreateMethodGetter)
                            var il = netMethod.Body.GetILProcessor();
                            il.Emit(OpCodes.Nop);
                            // Should not need to be imported, since it already exists in this type! (most of the time?)
                            var importType = netSDKDefinition.Module.ImportReference(getMethodProperty.GetMethod);
                            if (importType != null)
                                il.Emit(OpCodes.Call, importType);
                            else
                                il.Emit(OpCodes.Call, getMethodProperty.GetMethod);

                            // Now we need to provide all of the arguments necessary
                            // If this is a non-static method, ldarg0 is the 'this' parameter
                            // Otherwise, ldarg_1 is the first parameter
                            // TODO: Fix generics eventually
                            // First arg is the first non-instance argument
                            int firstArg = 0;
                            if (methodDefinition.IsStatic)
                            {
                                il.Emit(OpCodes.Ldnull);
                            }
                            else
                            {
                                il.Emit(OpCodes.Ldarg_0);
                                firstArg = 1;
                            }
                            // The netMethod's parameters are identical to the IL2CPP_Method's parameters
                            // If the method has 0 parameters, we need to call the instance invoke with either a null IL2CPP_Object or non-null
                            // depending on if the method is static or not
                            // If the method has more than 0 parameters, we need to call the instance invoke with either a null IL2CPP_Object, or non-null
                            // depending on if the method is static or not followed by each of the arguments
                            if (methodDefinition.Parameters.Count == 0)
                            {
                                il.Emit(OpCodes.Callvirt, netSDKDefinition.Module.ImportReference(netSDKInvoke0args));
                            }
                            else
                            {
                                // Load the length of the parameters
                                EmitHelper.LdcI4(il, netMethod.Parameters.Count - firstArg);
                                il.Emit(OpCodes.Newarr, netSDKDefinition.Module.ImportReference(typeof(object)));
                                for (int pi = firstArg; pi < netMethod.Parameters.Count; pi++)
                                {
                                    il.Emit(OpCodes.Dup);
                                    EmitHelper.LdcI4(il, pi - firstArg);
                                    EmitHelper.Ldarg(il, pi);
                                    if (netMethod.Parameters[pi].ParameterType.IsPrimitive)
                                        il.Emit(OpCodes.Box, netSDKDefinition.Module.ImportReference(netMethod.Parameters[pi].ParameterType));
                                    il.Emit(OpCodes.Stelem_Ref);
                                }
                                il.Emit(OpCodes.Callvirt, netSDKDefinition.Module.ImportReference(netSDKInvoke));
                            }

                            // If we are returning a primitive or a value type, we need to unbox it.
                            // Otherwise, we need to just need to stloc2, br.s to the last two instructions
                            bool success = true;
                            if (isPrimitive)
                            {
                                il.Emit(OpCodes.Call, netSDKDefinition.Module.ImportReference(netSDKUnbox));
                                // The unboxed value is a void*, we deref it according to the type of the actual return
                                switch (netMethod.ReturnType.MetadataType)
                                {
                                    case MetadataType.Boolean:
                                    case MetadataType.SByte:
                                        il.Emit(OpCodes.Ldind_I1);
                                        break;
                                    case MetadataType.Byte:
                                        il.Emit(OpCodes.Ldind_U1);
                                        break;
                                    case MetadataType.Char:
                                    case MetadataType.UInt16:
                                        il.Emit(OpCodes.Ldind_U2);
                                        break;
                                    case MetadataType.Double:
                                        il.Emit(OpCodes.Ldind_R8);
                                        break;
                                    case MetadataType.Int16:
                                        il.Emit(OpCodes.Ldind_I2);
                                        break;
                                    case MetadataType.Int32:
                                        il.Emit(OpCodes.Ldind_I4);
                                        break;
                                    case MetadataType.Int64:
                                        il.Emit(OpCodes.Ldind_I8);
                                        break;
                                    case MetadataType.Single:
                                        il.Emit(OpCodes.Ldind_R4);
                                        break;
                                    case MetadataType.UInt32:
                                        il.Emit(OpCodes.Ldind_U4);
                                        break;
                                    case MetadataType.UInt64:
                                        il.Emit(OpCodes.Ldind_I8);
                                        break;
                                    default:
                                        // We have failed! In this case, we will (in the future) attempt to convert the method
                                        // back to an IL2CPP_Object
                                        // but for now, we will just never add this method to the netSDK type's methods, that way
                                        // even though the IL is all garbage, it isn't an issue.
                                        success = false;
                                        break;
                                }
                            }
                            else if (isStringReturn)
                            {
                                il.Emit(OpCodes.Call, netSDKDefinition.Module.ImportReference(netSDKUnboxString));
                            }
                            il.Emit(OpCodes.Stloc_2);
                            var branchEnd = Instruction.Create(OpCodes.Ldloc_2);
                            il.Emit(OpCodes.Br_S, branchEnd);
                            il.Append(branchEnd);
                            il.Emit(OpCodes.Ret);
                            if (success)
                                netSDKDefinition.Methods.Add(netMethod);
                        }
                        end:;
                    }
                    //property
                    var propertyEnd = typeDef.propertyStart + typeDef.property_count;
                    for (var i = typeDef.propertyStart; i < propertyEnd; ++i)
                    {
                        var propertyDef = metadata.propertyDefs[i];
                        var propertyName = metadata.GetStringFromIndex(propertyDef.nameIndex);
                        TypeReference propertyType = null;
                        MethodDefinition GetMethod = null;
                        MethodDefinition SetMethod = null;
                        if (propertyDef.get >= 0)
                        {
                            GetMethod = methodDefinitionDic[typeDef.methodStart + propertyDef.get];
                            propertyType = GetMethod.ReturnType;
                        }
                        if (propertyDef.set >= 0)
                        {
                            SetMethod = methodDefinitionDic[typeDef.methodStart + propertyDef.set];
                            if (propertyType == null)
                                propertyType = SetMethod.Parameters[0].ParameterType;
                        }
                        var propertyDefinition = new PropertyDefinition(propertyName, (PropertyAttributes)propertyDef.attrs, propertyType)
                        {
                            GetMethod = GetMethod,
                            SetMethod = SetMethod
                        };
                        typeDefinition.Properties.Add(propertyDefinition);
                        propertyDefinitionDic.Add(i, propertyDefinition);
                    }
                    //event
                    var eventEnd = typeDef.eventStart + typeDef.event_count;
                    for (var i = typeDef.eventStart; i < eventEnd; ++i)
                    {
                        var eventDef = metadata.eventDefs[i];
                        var eventName = metadata.GetStringFromIndex(eventDef.nameIndex);
                        var eventType = il2Cpp.types[eventDef.typeIndex];
                        var eventTypeRef = GetTypeReference(typeDefinition, eventType);
                        var eventDefinition = new EventDefinition(eventName, (EventAttributes)eventType.attrs, eventTypeRef);
                        if (eventDef.add >= 0)
                            eventDefinition.AddMethod = methodDefinitionDic[typeDef.methodStart + eventDef.add];
                        if (eventDef.remove >= 0)
                            eventDefinition.RemoveMethod = methodDefinitionDic[typeDef.methodStart + eventDef.remove];
                        if (eventDef.raise >= 0)
                            eventDefinition.InvokeMethod = methodDefinitionDic[typeDef.methodStart + eventDef.raise];
                        typeDefinition.Events.Add(eventDefinition);
                        eventDefinitionDic.Add(i, eventDefinition);
                    }
                    //补充泛型参数
                    if (typeDef.genericContainerIndex >= 0)
                    {
                        var genericContainer = metadata.genericContainers[typeDef.genericContainerIndex];
                        if (genericContainer.type_argc > typeDefinition.GenericParameters.Count)
                        {
                            for (int i = 0; i < genericContainer.type_argc; i++)
                            {
                                var genericParameterIndex = genericContainer.genericParameterStart + i;
                                if (!genericParameterDic.TryGetValue(genericParameterIndex, out var genericParameter))
                                {
                                    CreateGenericParameter(genericParameterIndex, typeDefinition);
                                }
                                else
                                {
                                    if (!typeDefinition.GenericParameters.Contains(genericParameter))
                                    {
                                        typeDefinition.GenericParameters.Add(genericParameter);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            //第三遍，添加CustomAttribute
            if (il2Cpp.version > 20)
            {
                PrepareCustomAttribute();
                foreach (var imageDef in metadata.imageDefs)
                {
                    var typeEnd = imageDef.typeStart + imageDef.typeCount;
                    for (int index = imageDef.typeStart; index < typeEnd; index++)
                    {
                        var typeDef = metadata.typeDefs[index];
                        var typeDefinition = typeDefinitionDic[index];
                        //typeAttribute
                        CreateCustomAttribute(imageDef, typeDef.customAttributeIndex, typeDef.token, typeDefinition.Module, typeDefinition.CustomAttributes);

                        //field
                        var fieldEnd = typeDef.fieldStart + typeDef.field_count;
                        for (var i = typeDef.fieldStart; i < fieldEnd; ++i)
                        {
                            var fieldDef = metadata.fieldDefs[i];
                            var fieldDefinition = fieldDefinitionDic[i];
                            //fieldAttribute
                            CreateCustomAttribute(imageDef, fieldDef.customAttributeIndex, fieldDef.token, typeDefinition.Module, fieldDefinition.CustomAttributes);
                        }

                        //method
                        var methodEnd = typeDef.methodStart + typeDef.method_count;
                        for (var i = typeDef.methodStart; i < methodEnd; ++i)
                        {
                            var methodDef = metadata.methodDefs[i];
                            var methodDefinition = methodDefinitionDic[i];
                            //methodAttribute
                            CreateCustomAttribute(imageDef, methodDef.customAttributeIndex, methodDef.token, typeDefinition.Module, methodDefinition.CustomAttributes);

                            //method parameter
                            for (var j = 0; j < methodDef.parameterCount; ++j)
                            {
                                var parameterDef = metadata.parameterDefs[methodDef.parameterStart + j];
                                var parameterDefinition = parameterDefinitionDic[methodDef.parameterStart + j];
                                //parameterAttribute
                                CreateCustomAttribute(imageDef, parameterDef.customAttributeIndex, parameterDef.token, typeDefinition.Module, parameterDefinition.CustomAttributes);
                            }
                        }

                        //property
                        var propertyEnd = typeDef.propertyStart + typeDef.property_count;
                        for (var i = typeDef.propertyStart; i < propertyEnd; ++i)
                        {
                            var propertyDef = metadata.propertyDefs[i];
                            var propertyDefinition = propertyDefinitionDic[i];
                            //propertyAttribute
                            CreateCustomAttribute(imageDef, propertyDef.customAttributeIndex, propertyDef.token, typeDefinition.Module, propertyDefinition.CustomAttributes);
                        }

                        //event
                        var eventEnd = typeDef.eventStart + typeDef.event_count;
                        for (var i = typeDef.eventStart; i < eventEnd; ++i)
                        {
                            var eventDef = metadata.eventDefs[i];
                            var eventDefinition = eventDefinitionDic[i];
                            //eventAttribute
                            CreateCustomAttribute(imageDef, eventDef.customAttributeIndex, eventDef.token, typeDefinition.Module, eventDefinition.CustomAttributes);
                        }
                    }
                }
            }
        }

        private TypeReference GetTypeReferenceWithByRef(MemberReference memberReference, Il2CppType il2CppType)
        {
            var typeReference = GetTypeReference(memberReference, il2CppType);
            if (il2CppType.byref == 1)
            {
                return new ByReferenceType(typeReference);
            }
            else
            {
                return typeReference;
            }
        }

        private TypeReference GetTypeReference(MemberReference memberReference, Il2CppType il2CppType)
        {
            var moduleDefinition = memberReference.Module;
            switch (il2CppType.type)
            {
                case Il2CppTypeEnum.IL2CPP_TYPE_OBJECT:
                    return moduleDefinition.ImportReference(typeof(object));
                case Il2CppTypeEnum.IL2CPP_TYPE_VOID:
                    return moduleDefinition.ImportReference(typeof(void));
                case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                    return moduleDefinition.ImportReference(typeof(bool));
                case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                    return moduleDefinition.ImportReference(typeof(char));
                case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                    return moduleDefinition.ImportReference(typeof(sbyte));
                case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                    return moduleDefinition.ImportReference(typeof(byte));
                case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                    return moduleDefinition.ImportReference(typeof(short));
                case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                    return moduleDefinition.ImportReference(typeof(ushort));
                case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                    return moduleDefinition.ImportReference(typeof(int));
                case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                    return moduleDefinition.ImportReference(typeof(uint));
                case Il2CppTypeEnum.IL2CPP_TYPE_I:
                    return moduleDefinition.ImportReference(typeof(IntPtr));
                case Il2CppTypeEnum.IL2CPP_TYPE_U:
                    return moduleDefinition.ImportReference(typeof(UIntPtr));
                case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                    return moduleDefinition.ImportReference(typeof(long));
                case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                    return moduleDefinition.ImportReference(typeof(ulong));
                case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                    return moduleDefinition.ImportReference(typeof(float));
                case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                    return moduleDefinition.ImportReference(typeof(double));
                case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                    return moduleDefinition.ImportReference(typeof(string));
                case Il2CppTypeEnum.IL2CPP_TYPE_TYPEDBYREF:
                    return moduleDefinition.ImportReference(typeof(TypedReference));
                case Il2CppTypeEnum.IL2CPP_TYPE_CLASS:
                case Il2CppTypeEnum.IL2CPP_TYPE_VALUETYPE:
                    {
                        var typeDefinition = typeDefinitionDic[il2CppType.data.klassIndex];
                        return moduleDefinition.ImportReference(typeDefinition);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_ARRAY:
                    {
                        var arrayType = il2Cpp.MapVATR<Il2CppArrayType>(il2CppType.data.array);
                        var oriType = il2Cpp.GetIl2CppType(arrayType.etype);
                        return new ArrayType(GetTypeReference(memberReference, oriType), arrayType.rank);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_GENERICINST:
                    {
                        var genericClass = il2Cpp.MapVATR<Il2CppGenericClass>(il2CppType.data.generic_class);
                        var typeDefinition = typeDefinitionDic[genericClass.typeDefinitionIndex];
                        var genericInstanceType = new GenericInstanceType(moduleDefinition.ImportReference(typeDefinition));
                        var genericInst = il2Cpp.MapVATR<Il2CppGenericInst>(genericClass.context.class_inst);
                        var pointers = il2Cpp.ReadPointers(genericInst.type_argv, genericInst.type_argc);
                        foreach (var pointer in pointers)
                        {
                            var oriType = il2Cpp.GetIl2CppType(pointer);
                            genericInstanceType.GenericArguments.Add(GetTypeReference(memberReference, oriType));
                        }
                        return genericInstanceType;
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_SZARRAY:
                    {
                        var oriType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        return new ArrayType(GetTypeReference(memberReference, oriType));
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_VAR:
                    {
                        if (genericParameterDic.TryGetValue(il2CppType.data.genericParameterIndex, out var genericParameter))
                        {
                            return genericParameter;
                        }
                        if (memberReference is MethodDefinition methodDefinition)
                        {
                            return CreateGenericParameter(il2CppType.data.genericParameterIndex, methodDefinition.DeclaringType);
                        }
                        var typeDefinition = (TypeDefinition)memberReference;
                        return CreateGenericParameter(il2CppType.data.genericParameterIndex, typeDefinition);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_MVAR:
                    {
                        if (genericParameterDic.TryGetValue(il2CppType.data.genericParameterIndex, out var genericParameter))
                        {
                            return genericParameter;
                        }
                        var methodDefinition = (MethodDefinition)memberReference;
                        return CreateGenericParameter(il2CppType.data.genericParameterIndex, methodDefinition);
                    }
                case Il2CppTypeEnum.IL2CPP_TYPE_PTR:
                    {
                        var oriType = il2Cpp.GetIl2CppType(il2CppType.data.type);
                        return new PointerType(GetTypeReference(memberReference, oriType));
                    }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private object GetDefaultValue(int dataIndex, int typeIndex)
        {
            var pointer = metadata.GetDefaultValueFromIndex(dataIndex);
            if (pointer > 0)
            {
                var defaultValueType = il2Cpp.types[typeIndex];
                metadata.Position = pointer;
                switch (defaultValueType.type)
                {
                    case Il2CppTypeEnum.IL2CPP_TYPE_BOOLEAN:
                        return metadata.ReadBoolean();
                    case Il2CppTypeEnum.IL2CPP_TYPE_U1:
                        return metadata.ReadByte();
                    case Il2CppTypeEnum.IL2CPP_TYPE_I1:
                        return metadata.ReadSByte();
                    case Il2CppTypeEnum.IL2CPP_TYPE_CHAR:
                        return BitConverter.ToChar(metadata.ReadBytes(2), 0);
                    case Il2CppTypeEnum.IL2CPP_TYPE_U2:
                        return metadata.ReadUInt16();
                    case Il2CppTypeEnum.IL2CPP_TYPE_I2:
                        return metadata.ReadInt16();
                    case Il2CppTypeEnum.IL2CPP_TYPE_U4:
                        return metadata.ReadUInt32();
                    case Il2CppTypeEnum.IL2CPP_TYPE_I4:
                        return metadata.ReadInt32();
                    case Il2CppTypeEnum.IL2CPP_TYPE_U8:
                        return metadata.ReadUInt64();
                    case Il2CppTypeEnum.IL2CPP_TYPE_I8:
                        return metadata.ReadInt64();
                    case Il2CppTypeEnum.IL2CPP_TYPE_R4:
                        return metadata.ReadSingle();
                    case Il2CppTypeEnum.IL2CPP_TYPE_R8:
                        return metadata.ReadDouble();
                    case Il2CppTypeEnum.IL2CPP_TYPE_STRING:
                        var len = metadata.ReadInt32();
                        return Encoding.UTF8.GetString(metadata.ReadBytes(len));
                }
            }
            return null;
        }

        private void PrepareCustomAttribute()
        {
            var attributeNames = new[]
            {
                //"System.Runtime.CompilerServices.CompilerGeneratedAttribute",
                "System.Runtime.CompilerServices.ExtensionAttribute",
                "System.Runtime.CompilerServices.NullableAttribute",
                "System.Runtime.CompilerServices.NullableContextAttribute",
                "System.Runtime.CompilerServices.IsReadOnlyAttribute", //in关键字
                "System.Diagnostics.DebuggerHiddenAttribute",
                "System.Diagnostics.DebuggerStepThroughAttribute",
                // Type attributes:
                "System.FlagsAttribute",
                "System.Runtime.CompilerServices.IsByRefLikeAttribute",
                // Field attributes:
                "System.NonSerializedAttribute",
                // Method attributes:
                "System.Runtime.InteropServices.PreserveSigAttribute",
                // Parameter attributes:
                "System.ParamArrayAttribute",
                "System.Runtime.CompilerServices.CallerMemberNameAttribute",
                "System.Runtime.CompilerServices.CallerFilePathAttribute",
                "System.Runtime.CompilerServices.CallerLineNumberAttribute",
                // Type parameter attributes:
                "System.Runtime.CompilerServices.IsUnmanagedAttribute",
                // Unity
                "UnityEngine.SerializeField" //MonoBehaviour的反序列化
            };
            foreach (var attributeName in attributeNames)
            {
                foreach (var assemblyDefinition in Assemblies)
                {
                    var attributeType = assemblyDefinition.MainModule.GetType(attributeName);
                    if (attributeType != null)
                    {
                        var ctor = attributeType.Methods.FirstOrDefault(x => x.Name == ".ctor");
                        if (ctor == null)
                            continue;
                        knownAttributes.Add(attributeName, ctor);
                        break;
                    }
                }
            }
        }

        private void CreateCustomAttribute(Il2CppImageDefinition imageDef, int customAttributeIndex, uint token, ModuleDefinition moduleDefinition, Collection<CustomAttribute> customAttributes)
        {
            var attributeIndex = metadata.GetCustomAttributeIndex(imageDef, customAttributeIndex, token);
            if (attributeIndex >= 0)
            {
                var attributeTypeRange = metadata.attributeTypeRanges[attributeIndex];
                for (int i = 0; i < attributeTypeRange.count; i++)
                {
                    var attributeTypeIndex = metadata.attributeTypes[attributeTypeRange.start + i];
                    var attributeType = il2Cpp.types[attributeTypeIndex];
                    var typeDefinition = typeDefinitionDic[attributeType.data.klassIndex];
                    if (knownAttributes.TryGetValue(typeDefinition.FullName, out var methodDefinition))
                    {
                        var customAttribute = new CustomAttribute(moduleDefinition.ImportReference(methodDefinition));
                        customAttributes.Add(customAttribute);
                    }
                    else
                    {
                        var methodPointer = il2Cpp.customAttributeGenerators[attributeIndex];
                        var fixedMethodPointer = il2Cpp.FixPointer(methodPointer);
                        var customAttribute = new CustomAttribute(moduleDefinition.ImportReference(attributeAttribute));
                        var name = new CustomAttributeNamedArgument("Name", new CustomAttributeArgument(stringType, typeDefinition.Name));
                        var rva = new CustomAttributeNamedArgument("RVA", new CustomAttributeArgument(stringType, $"0x{fixedMethodPointer:X}"));
                        var offset = new CustomAttributeNamedArgument("Offset", new CustomAttributeArgument(stringType, $"0x{il2Cpp.MapVATR(methodPointer):X}"));
                        customAttribute.Fields.Add(name);
                        customAttribute.Fields.Add(rva);
                        customAttribute.Fields.Add(offset);
                        customAttributes.Add(customAttribute);
                    }
                }
            }
        }

        private GenericParameter CreateGenericParameter(long genericParameterIndex, IGenericParameterProvider iGenericParameterProvider)
        {
            var param = metadata.genericParameters[genericParameterIndex];
            var genericName = metadata.GetStringFromIndex(param.nameIndex);
            var genericParameter = new GenericParameter(genericName, iGenericParameterProvider);
            genericParameter.Attributes = (GenericParameterAttributes)param.flags;
            iGenericParameterProvider.GenericParameters.Add(genericParameter);
            genericParameterDic.Add(genericParameterIndex, genericParameter);
            for (int i = 0; i < param.constraintsCount; ++i)
            {
                var il2CppType = il2Cpp.types[metadata.constraintIndices[param.constraintsStart + i]];
                genericParameter.Constraints.Add(new GenericParameterConstraint(GetTypeReference((MemberReference)iGenericParameterProvider, il2CppType)));
            }
            return genericParameter;
        }
    }
}
