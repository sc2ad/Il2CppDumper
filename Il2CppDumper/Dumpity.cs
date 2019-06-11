using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Il2CppDumper
{
    public class Dumpity
    {
        public abstract class DumpityMethod
        {
            public List<FieldDefinition> Fields { get; set; }
            public TypeDefinition ParentType { get; private set; }
            public DumpityMethod(TypeDefinition type)
            {
                Fields = new List<FieldDefinition>();
                ParentType = type;
            }
            public virtual void Add(FieldDefinition f)
            {
                Fields.Add(f);
            }
            public bool Has()
            {
                return ParentType.Methods.Any(m => m.Name == MethodName() && m.Attributes == Attributes() && m.ReturnType == ReturnType());
            }
            public MethodDefinition Get()
            {
                return ParentType.Methods.First(m => m.Name == MethodName() && m.Attributes == Attributes() && m.ReturnType == ReturnType());
            }
            public abstract string MethodName();
            public abstract Mono.Cecil.MethodAttributes Attributes();
            public abstract TypeReference ReturnType();
            public abstract ParameterDefinition[] Parameters();
            public abstract MethodDefinition ToMethod(); 
        }
        public class DumpityReadMethod : DumpityMethod
        {
            private const Type READER = typeof(CustomBinaryReader);
            private const Type ASSET_PTR_TYPE = typeof(AssetPtr);
            private TypeReference serializeFieldAttr;

            public DumpityReadMethod(TypeDefinition type, TypeReference serializeField) : base(type)
            {
                serializeFieldAttr = serializeField;
            }

            public override string MethodName()
            {
                return "ReadFrom";
            }

            public override Mono.Cecil.MethodAttributes Attributes()
            {
                return Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static;
            }

            public override TypeReference ReturnType()
            {
                return ParentType;
            }

            public override ParameterDefinition[] Parameters()
            {
                return new ParameterDefinition[]
                {
                    new ParameterDefinition("reader", Mono.Cecil.ParameterAttributes.None, ParentType.Module.Import(READER))
                };
            }

            private MethodInfo GetReadPrimitive(MetadataType type)
            {
                string methodName = "";
                switch (type)
                {
                    case MetadataType.Boolean:
                        methodName = "ReadAlignedBool";
                        break;
                    case MetadataType.Byte:
                        methodName = "ReadAlignedByte";
                        break;
                    case MetadataType.Char:
                        methodName = "ReadAlignedChar";
                        break;
                    case MetadataType.String:
                        methodName = "ReadAlignedString";
                        break;
                    case MetadataType.Single:
                        methodName = "ReadSingle";
                        break;
                    case MetadataType.Double:
                        methodName = "ReadDouble";
                        break;
                    case MetadataType.Int16:
                        methodName = "ReadInt16";
                        break;
                    case MetadataType.Int32:
                        methodName = "ReadInt32";
                        break;
                    case MetadataType.Int64:
                        methodName = "ReadInt64";
                        break;
                    case MetadataType.UInt16:
                        methodName = "ReadUInt16";
                        break;
                    case MetadataType.UInt32:
                        methodName = "ReadUInt32";
                        break;
                    case MetadataType.UInt64:
                        methodName = "ReadUInt64";
                        break;
                }
                return READER.GetMethod(methodName);
            }

            private void WriteReadPrimitive(ILProcessor worker, MethodDefinition method, TypeDefinition thisType, FieldDefinition f)
            {
                var r = GetReadPrimitive(f.FieldType.MetadataType);
                // Write a primitive read line
                var callCode = worker.Create(OpCodes.Callvirt, thisType.Module.Import(r));
                f.Module.Import(r.ReturnType);
                // Duplicate the reference
                worker.Emit(OpCodes.Dup);
                // Put Reader onto stack
                worker.Emit(OpCodes.Ldarg, method.Parameters[0]);
                // Call reader.ReadSOMEPRIMITIVE
                worker.Append(callCode);
                // Set the field of the object 
                worker.Emit(OpCodes.Stfld, f);
                Console.WriteLine($"Writing {f.Name} as {f.FieldType}");
                f.IsPublic = true;
                f.IsPrivate = false;
            }

            private void WriteReadPointer(ILProcessor worker, MethodDefinition method, FieldDefinition f)
            {
                // ASSUMING THE LOCAL FIELD F IS A POINTER!
                // Write the read aligned string line
                var callCode = worker.Create(OpCodes.Newobj, f.Module.Import(ASSET_PTR_TYPE.GetConstructor(new Type[] { READER })));
                // Duplicate the reference
                worker.Emit(OpCodes.Dup);
                // Put Reader onto stack
                worker.Emit(OpCodes.Ldarg, method.Parameters[0]);
                // Call reader.ReadAlignedString
                worker.Append(callCode);
                // Set the field of the object 
                worker.Emit(OpCodes.Stfld, f);
            }

            private void WriteReadClass(ILProcessor worker, MethodDefinition method, FieldDefinition f, MethodDefinition read)
            {
                // Write the read object line
                var callCode = worker.Create(OpCodes.Call, f.Module.Import(read));
                // Duplicate the reference
                worker.Emit(OpCodes.Dup);
                // Put Reader onto stack
                worker.Emit(OpCodes.Ldarg, method.Parameters[0]);
                // Put length onto stack
                worker.Emit(OpCodes.Ldc_I4_0);
                // Call SomeObject.ReadFrom()
                worker.Append(callCode);
                // Set the field of the object 
                worker.Emit(OpCodes.Stfld, f);
            }

            private void WriteReadClassArray(ILProcessor worker, MethodDefinition method, FieldDefinition f, TypeDefinition t, MethodReference read)
            {
                var r = READER.GetMethod("ReadPrefixedArray", Type.EmptyTypes);
                var m = f.Module.Import(r);
                //f.Module.Import(r.ReturnType);
                // Write the read object line
                var callCode = worker.Create(OpCodes.Call, m.MakeGeneric(f.Module.Import(t)));
                // Duplicate the reference
                worker.Emit(OpCodes.Dup);
                // Put the reader onto the stack
                worker.Emit(OpCodes.Ldarg, method.Parameters[0]);
                // Call ReadPrefixedArray()
                worker.Append(callCode);
                // Set the field
                worker.Emit(OpCodes.Stfld, f);
                //worker.Emit(OpCodes.Dup);
            }

            private void WriteReadStruct(ILProcessor worker, MethodDefinition method, FieldDefinition f, TypeDefinition s)
            {
                var structVar = new VariableDefinition(f.Module.Import(s));

                method.Body.Variables.Add(structVar);
                method.Body.InitLocals = true;

                worker.Emit(OpCodes.Ldloca_S, structVar);
                worker.Emit(OpCodes.Initobj, f.Module.Import(s));

                foreach (var field in s.Fields)
                {
                    if (field.IsStatic || field.HasConstant)
                    {
                        // The field is either static or constant, so don't serialize it
                        continue;
                    }
                    if (DumpityUtils.IsPrimitive(field.FieldType))
                    {
                        var m = f.Module.Import(GetReadPrimitive(field.FieldType.MetadataType));
                        f.Module.Import(field.FieldType);
                        // Ldloca
                        worker.Emit(OpCodes.Ldloca_S, structVar);
                        // Load reader
                        worker.Emit(OpCodes.Ldarg_0);
                        // Call m
                        worker.Emit(OpCodes.Call, m);
                        // Set field
                        worker.Emit(OpCodes.Stfld, f.Module.Import(field));
                    }
                    else
                    {
                        // Field might be an enum!
                        if (field.FieldType.MetadataType == MetadataType.ValueType)
                        {
                            var m = f.Module.Import(READER.GetMethod("ReadInt32"));
                            f.Module.Import(field.FieldType);
                            // Ldloca
                            worker.Emit(OpCodes.Ldloca_S, structVar);
                            // Load reader
                            worker.Emit(OpCodes.Ldarg_0);
                            // Call m
                            worker.Emit(OpCodes.Call, m);
                            // Set field
                            worker.Emit(OpCodes.Stfld, f.Module.Import(field));
                        }
                        else
                        {
                            throw new Exception("Field in struct is unknown!");
                        }
                    }
                }
                worker.Emit(OpCodes.Dup);
                worker.Emit(OpCodes.Ldloc_S, structVar);
                worker.Emit(OpCodes.Stfld, f);
            }

            private void WriteReadOther(ILProcessor worker, MethodDefinition method, FieldDefinition f)
            {
                // If the value is a string, class; we need to read it right away.
                // String = AlignedString (most of the time? Always? Not sure)
                // Class = Pointer, ONLY WHEN THE CLASS DOES NOT HAVE SERIALIZABLE ATTRIBUTE
                var type = f.FieldType.MetadataType;
                switch (type)
                {
                    case MetadataType.ValueType:
                        // Structs are always serialized
                        if (!f.FieldType.FullName.Contains("UnityEngine"))
                        {
                            break;
                        }
                        Console.WriteLine($"Writing {f.Name} as struct with type: {f.FieldType}");
                        f.IsPublic = true;
                        f.IsPrivate = false;
                        WriteReadStruct(worker, method, f, f.FieldType.Resolve());
                        break;
                    case MetadataType.Class:
                        Console.WriteLine($"{f.FieldType.FullName} is the type of field: {f.Name}");
                        if (DumpityUtils.IsSerializable(f.FieldType.Resolve()))
                        {
                            Console.WriteLine($"Serializable class found: {f.FieldType.FullName}");
                            f.IsPublic = true;
                            f.IsPrivate = false;
                            // Create a read method if it doesn't exist already in that class.
                            var readM = new DumpityReadMethod(f.FieldType.Resolve())
                            {
                                Fields = DumpityUtils.FindSerializedData(f.FieldType.Resolve(), serializeFieldAttr)
                            };
                            var readMethod = readM.ToMethod();
                            WriteReadClass(worker, method, f, readMethod);
                        }
                        else
                        {
                            // No custom attributes.
                            // This should be a pointer.
                            // Need to add a field and make it public here.
                            Console.WriteLine($"Writing {f.Name} as a pointer with attributes: {f.Attributes}");
                            // Create the public field for the pointer!
                            var assetF = new FieldDefinition(f.Name + "Ptr", Mono.Cecil.FieldAttributes.Public, f.Module.Import(ASSET_PTR_TYPE));
                            f.DeclaringType.Fields.Add(assetF);
                            WriteReadPointer(worker, method, assetF);
                        }
                        break;
                    case MetadataType.Array:
                        var t = f.FieldType.Resolve().GetElementType().Resolve();
                        Console.WriteLine($"{t.FullName} is the type in the array at field: {f.Name}");
                        f.IsPublic = true;
                        f.IsPrivate = false;
                        // Need to now recursively call this function, except on the TypeDefinition for the new classes.

                        // If the type is a serializable class, then we already wrote (or will write) a method for it.

                        if (DumpityUtils.IsSerializable(t))
                        {
                            // Call the object's ReadFrom method for each item
                            if (DumpityUtils.IsPrimitive(t))
                            {
                                // Primitive, use that. Don't recurse.
                                var readMethod = GetReadPrimitive(t.MetadataType);
                                Console.WriteLine($"Writing {f.Name} as an Array of {t.Name} (PRIMITIVE)");
                                WriteReadClassArray(worker, method, f, t, f.Module.Import(readMethod));
                            }
                            else
                            {
                                Console.WriteLine($"Writing {t.Name} as an Object to read/write from!");
                                // Non-primitive, recurse
                                var readM = new DumpityReadMethod(t)
                                {
                                    Fields = DumpityUtils.FindSerializedData(t, serializeFieldAttr),
                                };
                                var readMethod = readM.ToMethod();
                                Console.WriteLine($"Writing {f.Name} as an Array of {t.Name}");
                                WriteReadClassArray(worker, method, f, t, readMethod);
                            }

                            //worker.Emit(OpCodes.Dup));
                            //worker.Emit())
                        }
                        else
                        {
                            // Otherwise, read a pointer for each item.
                            Console.WriteLine($"Writing {t.Name} as a pointer!");
                        }

                        break;
                }
            }

            public override MethodDefinition ToMethod()
            {
                if (Has())
                    return Get();
                var method = new MethodDefinition(MethodName(), Attributes(), ReturnType());
                foreach (var p in Parameters())
                {
                    method.Parameters.Add(p);
                }
                ILProcessor worker = method.Body.GetILProcessor();

                var constructor = DumpityUtils.GetConstructor(ParentType);
                // Create local object
                worker.Emit(OpCodes.Newobj, constructor);

                foreach (var f in Fields)
                {
                    if (DumpityUtils.IsPrimitive(f.FieldType))
                    {
                        WriteReadPrimitive(worker, method, ParentType, f);
                    }
                    else
                    {
                        WriteReadOther(worker, method, f);
                    }
                }
                worker.Emit(OpCodes.Ret);
                return method;
            }
        }
    }
}
