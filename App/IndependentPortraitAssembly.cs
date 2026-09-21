using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text;

namespace JixModMaker;

internal static class IndependentPortraitAssembly
{
    private const string ResourceNamespace = "Jix.DynamicPortrait";
    private const string MapName = "JixMapAnimatedPortrait";
    private const string LoaderName = "JixLoadIndependentPortrait";
    public static string Key(string texture) => "JixPortrait_" + texture;

    public static void Patch(ModuleDefinition module, string texture, byte[] video, PortraitMovieMetadata metadata)
    {
        var config = module.Types.Single(t => t.FullName == "SkinStandingPaintingConfigureItem");
        var manager = module.Types.Single(t => t.FullName == "UI.CriMovieManager");
        var map = config.Methods.SingleOrDefault(m => m.Name == MapName);
        var loader = manager.Methods.SingleOrDefault(m => m.Name == LoaderName);
        if (map != null && loader == null)
            throw new InvalidOperationException("检测到旧版借用异画补丁。请先恢复旧版备份或选择干净资源，再制作独立动态立绘。");

        TypeReference Type(string fullName, IMetadataScope scope = null, bool value = false)
        {
            var existing = (TypeReference)module.GetType(fullName) ?? module.GetTypeReferences().FirstOrDefault(t => t.FullName == fullName);
            if (existing != null)
            {
                if (value) existing.IsValueType = true;
                return existing;
            }
            int separator = fullName.LastIndexOf('.');
            return new TypeReference(fullName[..separator], fullName[(separator + 1)..], module, scope ?? module.TypeSystem.CoreLibrary, value);
        }
        MethodReference Method(TypeReference owner, string name, TypeReference result, bool instance, params TypeReference[] parameters)
        {
            var method = new MethodReference(name, result, owner) { HasThis = instance };
            foreach (var parameter in parameters) method.Parameters.Add(new ParameterDefinition(parameter));
            return method;
        }
        var ts = module.TypeSystem;
        var equality = Method(ts.String, "op_Equality", ts.Boolean, false, ts.String, ts.String);
        var asset = Type("CriWare.Assets.CriManaUsmAsset");
        var unityObject = Type("UnityEngine.Object");
        var scriptable = Type("UnityEngine.ScriptableObject", unityObject.Scope);
        var assetBase = Type("CriWare.Assets.CriAssetBase", asset.Scope);
        var implementation = Type("CriWare.Assets.CriSerializedBytesAssetImpl", asset.Scope);
        var implementationInterface = Type("CriWare.Assets.ICriAssetImpl", asset.Scope);
        var systemType = Type("System.Type");
        var fieldInfo = Type("System.Reflection.FieldInfo");
        var flags = Type("System.Reflection.BindingFlags", value: true);
        var typeFromHandle = Method(systemType, "GetTypeFromHandle", systemType, false, Type("System.RuntimeTypeHandle", value: true));
        var getField = Method(systemType, "GetField", fieldInfo, true, ts.String, flags);
        var setValue = Method(fieldInfo, "SetValue", ts.Void, true, ts.Object, ts.Object);
        var getFieldType = Method(fieldInfo, "get_FieldType", systemType, true);
        var createObject = Method(Type("System.Activator"), "CreateInstance", ts.Object, false, systemType);
        var getType = Method(ts.Object, "GetType", systemType, true);
        var setName = Method(unityObject, "set_name", ts.Void, true, ts.String);

        string id = "Portrait_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texture)))[..24];
        var previous = module.Types.SingleOrDefault(t => t.Namespace == ResourceNamespace && t.Name == id);
        if (previous != null) module.Types.Remove(previous);
        var resource = new TypeDefinition(ResourceNamespace, id, TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed, ts.Object);
        module.Types.Add(resource);
        resource.Fields.Add(new FieldDefinition("Texture", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, ts.String) { Constant = texture });
        resource.Fields.Add(new FieldDefinition("Key", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, ts.String) { Constant = Key(texture) });
        resource.Fields.Add(new FieldDefinition("AspectRatio", FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault, ts.Single)
            { Constant = metadata.Width / (float)metadata.Height });
        // RVA data is supported by HybridCLR's interpreted images; manifest-resource streams are not assumed.
        var blobs = new List<FieldDefinition>();
        for (int offset = 0; offset < video.Length; offset += 65536)
        {
            int length = Math.Min(65536, video.Length - offset);
            string name = "Bytes" + length;
            var blobType = resource.NestedTypes.SingleOrDefault(t => t.Name == name);
            if (blobType == null)
            {
                blobType = new TypeDefinition("", name, TypeAttributes.NestedPrivate | TypeAttributes.ExplicitLayout | TypeAttributes.Sealed,
                    Type("System.ValueType")) { PackingSize = 1, ClassSize = length };
                resource.NestedTypes.Add(blobType);
            }
            var blob = new FieldDefinition("Video" + blobs.Count, FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly | FieldAttributes.HasFieldRVA, blobType)
                { InitialValue = video.AsSpan(offset, length).ToArray() };
            resource.Fields.Add(blob);
            blobs.Add(blob);
        }
        var create = new MethodDefinition("Create", MethodAttributes.Assembly | MethodAttributes.Static, asset);
        resource.Methods.Add(create);
        create.Body.InitLocals = true;
        var instance = new VariableDefinition(asset);
        var field = new VariableDefinition(fieldInfo);
        var info = new VariableDefinition(ts.Object);
        var bytes = new VariableDefinition(new ArrayType(ts.Byte));
        create.Body.Variables.Add(instance);
        create.Body.Variables.Add(field);
        create.Body.Variables.Add(info);
        create.Body.Variables.Add(bytes);
        var il = create.Body.GetILProcessor();
        il.Emit(OpCodes.Ldtoken, asset);
        il.Emit(OpCodes.Call, typeFromHandle);
        il.Emit(OpCodes.Call, Method(scriptable, "CreateInstance", scriptable, false, systemType));
        il.Emit(OpCodes.Castclass, asset);
        il.Emit(OpCodes.Stloc, instance);
        il.Emit(OpCodes.Ldloc, instance);
        il.Emit(OpCodes.Ldstr, Key(texture));
        il.Emit(OpCodes.Callvirt, setName);

        void FindField(TypeReference owner, string name)
        {
            il.Emit(OpCodes.Ldtoken, owner);
            il.Emit(OpCodes.Call, typeFromHandle);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_I4, 52); // Instance | Public | NonPublic, including serialized internal fields.
            il.Emit(OpCodes.Callvirt, getField);
        }
        void MetadataObject(string name, IEnumerable<(string Name, uint Value, bool Boolean)> values)
        {
            FindField(asset, name);
            il.Emit(OpCodes.Stloc, field);
            il.Emit(OpCodes.Ldloc, field);
            il.Emit(OpCodes.Callvirt, getFieldType);
            il.Emit(OpCodes.Call, createObject);
            il.Emit(OpCodes.Stloc, info);
            foreach (var value in values)
            {
                il.Emit(OpCodes.Ldloc, info);
                il.Emit(OpCodes.Callvirt, getType);
                il.Emit(OpCodes.Ldstr, value.Name);
                il.Emit(OpCodes.Ldc_I4, 20);
                il.Emit(OpCodes.Callvirt, getField);
                il.Emit(OpCodes.Ldloc, info);
                il.Emit(OpCodes.Ldc_I4, checked((int)value.Value));
                il.Emit(OpCodes.Box, value.Boolean ? ts.Boolean : ts.UInt32);
                il.Emit(OpCodes.Callvirt, setValue);
            }
            il.Emit(OpCodes.Ldloc, field);
            il.Emit(OpCodes.Ldloc, instance);
            il.Emit(OpCodes.Ldloc, info);
            il.Emit(OpCodes.Callvirt, setValue);
        }
        MetadataObject("movieInfo", new[] {
            ("width", metadata.Width, false), ("height", metadata.Height, false),
            ("dispWidth", metadata.Width, false), ("dispHeight", metadata.Height, false),
            ("framerateN", metadata.FramerateN, false), ("framerateD", metadata.FramerateD, false),
            ("totalFrames", metadata.TotalFrames, false), ("numAlphaStreams", 1u, false)
        });
        // Enum fields cannot be set with boxed uint values. Use Enum.ToObject for the native codec enum.
        foreach (string name in new[] { "codecType", "alphaCodecType" })
        {
            il.Emit(OpCodes.Ldloc, info);
            il.Emit(OpCodes.Callvirt, getType);
            il.Emit(OpCodes.Ldstr, name);
            il.Emit(OpCodes.Ldc_I4, 20);
            il.Emit(OpCodes.Callvirt, getField);
            il.Emit(OpCodes.Stloc, field);
            il.Emit(OpCodes.Ldloc, field);
            il.Emit(OpCodes.Ldloc, info);
            il.Emit(OpCodes.Ldloc, field);
            il.Emit(OpCodes.Callvirt, getFieldType);
            il.Emit(OpCodes.Ldc_I4_1); // SofdecPrime
            il.Emit(OpCodes.Call, Method(Type("System.Enum"), "ToObject", ts.Object, false, systemType, ts.Int32));
            il.Emit(OpCodes.Callvirt, setValue);
        }
        MetadataObject("assetInfo", new[] { ("loop", 1u, true) });
        il.Emit(OpCodes.Ldc_I4, video.Length);
        il.Emit(OpCodes.Newarr, ts.Byte);
        il.Emit(OpCodes.Stloc, bytes);
        for (int i = 0; i < blobs.Count; i++)
        {
            var blob = blobs[i];
            il.Emit(OpCodes.Ldc_I4, blob.InitialValue.Length);
            il.Emit(OpCodes.Newarr, ts.Byte);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldtoken, blob);
            il.Emit(OpCodes.Call, Method(Type("System.Runtime.CompilerServices.RuntimeHelpers"), "InitializeArray", ts.Void, false,
                Type("System.Array"), Type("System.RuntimeFieldHandle", value: true)));
            il.Emit(OpCodes.Ldc_I4_0);
            il.Emit(OpCodes.Ldloc, bytes);
            il.Emit(OpCodes.Ldc_I4, i * 65536);
            il.Emit(OpCodes.Ldc_I4, blob.InitialValue.Length);
            il.Emit(OpCodes.Call, Method(Type("System.Buffer"), "BlockCopy", ts.Void, false,
                Type("System.Array"), ts.Int32, Type("System.Array"), ts.Int32, ts.Int32));
        }
        FindField(assetBase, "implementation");
        il.Emit(OpCodes.Ldloc, instance);
        il.Emit(OpCodes.Ldloc, bytes);
        il.Emit(OpCodes.Newobj, Method(implementation, ".ctor", ts.Void, true, new ArrayType(ts.Byte)));
        il.Emit(OpCodes.Callvirt, setValue);
        il.Emit(OpCodes.Ldloc, instance);
        il.Emit(OpCodes.Callvirt, Method(assetBase, "get_Implementation", implementationInterface, true));
        il.Emit(OpCodes.Callvirt, Method(implementationInterface, "OnEnable", ts.Void, true));
        il.Emit(OpCodes.Ldloc, instance);
        il.Emit(OpCodes.Ret);

        var resources = module.Types.Where(t => t.Namespace == ResourceNamespace).ToArray();
        string Constant(TypeDefinition type, string name) => (string)type.Fields.Single(f => f.Name == name).Constant;
        var characterMethods = new[] { "GetCharacter", "GetCharacterInGame" }.Select(name => config.Methods.Single(m => m.Name == name && m.Parameters.Count == 0)).ToArray();
        var tupleType = characterMethods[0].ReturnType;
        if (characterMethods.Any(m => m.ReturnType.FullName != "System.ValueTuple`2<System.String,System.Boolean>"))
            throw new InvalidDataException("立绘方法签名已改变，停止导出。");
        MethodReference tupleConstructor;
        if (map == null)
        {
            var constructors = characterMethods.SelectMany(m => m.Body.Instructions).Where(i => i.OpCode == OpCodes.Newobj &&
                i.Operand is MethodReference mr && mr.DeclaringType.FullName == tupleType.FullName).ToArray();
            if (constructors.Length == 0 || characterMethods.Any(m => !m.Body.Instructions.Any(constructors.Contains)) || constructors.Any(i => i.Previous?.OpCode != OpCodes.Ldc_I4_0))
                throw new InvalidDataException("立绘逻辑与已验证版本不同，停止导出。");
            tupleConstructor = (MethodReference)constructors[0].Operand;
            map = new MethodDefinition(MapName, MethodAttributes.Private | MethodAttributes.Static, tupleType);
            map.Parameters.Add(new ParameterDefinition(ts.String));
            map.Parameters.Add(new ParameterDefinition(ts.Boolean));
            config.Methods.Add(map);
            foreach (var instruction in constructors) { instruction.OpCode = OpCodes.Call; instruction.Operand = map; }
        }
        else tupleConstructor = (MethodReference)map.Body.Instructions.First(i => i.OpCode == OpCodes.Newobj).Operand;
        map.Body = new MethodBody(map);
        il = map.Body.GetILProcessor();
        foreach (var item in resources)
        {
            var next = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, Constant(item, "Texture"));
            il.Emit(OpCodes.Call, equality);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldstr, Constant(item, "Key"));
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Newobj, tupleConstructor);
            il.Emit(OpCodes.Ret);
            il.Append(next);
        }
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, tupleConstructor);
        il.Emit(OpCodes.Ret);

        var load = manager.Methods.Single(m => m.Name == "Load" && m.Parameters.Count == 1);
        if (loader == null)
        {
            loader = new MethodDefinition(LoaderName, MethodAttributes.Private, asset);
            loader.Parameters.Add(new ParameterDefinition(ts.String));
            manager.Methods.Add(loader);
            var result = new VariableDefinition(asset);
            load.Body.Variables.Add(result);
            load.Body.InitLocals = true;
            var entry = load.Body.Instructions[0];
            var taskType = (GenericInstanceType)load.ReturnType;
            var taskConstructor = Method(taskType, ".ctor", ts.Void, true, taskType.ElementType.GenericParameters[0]);
            var prefix = new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Call, loader),
                Instruction.Create(OpCodes.Stloc, result), Instruction.Create(OpCodes.Ldloc, result), Instruction.Create(OpCodes.Brfalse, entry),
                Instruction.Create(OpCodes.Ldloc, result), Instruction.Create(OpCodes.Newobj, taskConstructor), Instruction.Create(OpCodes.Ret) };
            foreach (var instruction in prefix) load.Body.GetILProcessor().InsertBefore(entry, instruction);
        }
        loader.Body = new MethodBody(loader) { InitLocals = true };
        var cached = new VariableDefinition(asset);
        loader.Body.Variables.Add(cached);
        il = loader.Body.GetILProcessor();
        var cache = manager.Fields.Single(f => f.Name == "_loadedAssets");
        var dictionary = (GenericInstanceType)cache.FieldType;
        var keyParameter = dictionary.ElementType.GenericParameters[0];
        var valueParameter = dictionary.ElementType.GenericParameters[1];
        var tryGet = Method(dictionary, "TryGetValue", ts.Boolean, true, keyParameter, new ByReferenceType(valueParameter));
        var add = Method(dictionary, "Add", ts.Void, true, keyParameter, valueParameter);
        foreach (var item in resources)
        {
            var next = Instruction.Create(OpCodes.Nop);
            var done = Instruction.Create(OpCodes.Ldloc, cached);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldstr, Constant(item, "Key"));
            il.Emit(OpCodes.Call, equality);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, cache);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloca, cached);
            il.Emit(OpCodes.Callvirt, tryGet);
            il.Emit(OpCodes.Brtrue, done);
            il.Emit(OpCodes.Call, item.Methods.Single(m => m.Name == "Create"));
            il.Emit(OpCodes.Stloc, cached);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, cache);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldloc, cached);
            il.Emit(OpCodes.Callvirt, add);
            il.Append(done);
            il.Emit(OpCodes.Ret);
            il.Append(next);
        }
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ret);

        if (manager.Methods.All(m => m.Name != "JixReleaseIndependentPortrait"))
        {
            var releases = manager.Methods.Where(m => m.Name is "Clear" or "ClearOne").SelectMany(m => m.Body.Instructions)
                .Where(i => i.OpCode == OpCodes.Call && i.Operand is MethodReference mr && mr.Name == "Release" && mr.DeclaringType.FullName == "UnityEngine.AddressableAssets.Addressables").ToArray();
            if (releases.Length != 2) throw new InvalidDataException("视频释放逻辑已改变，停止导出。");
            var originalRelease = (MethodReference)releases[0].Operand;
            var release = new MethodDefinition("JixReleaseIndependentPortrait", MethodAttributes.Private | MethodAttributes.Static, ts.Void);
            release.Parameters.Add(new ParameterDefinition(asset));
            manager.Methods.Add(release);
            il = release.Body.GetILProcessor();
            var original = Instruction.Create(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Callvirt, Method(unityObject, "get_name", ts.String, true));
            il.Emit(OpCodes.Ldstr, "JixPortrait_");
            il.Emit(OpCodes.Ldc_I4_4); // Ordinal comparison
            il.Emit(OpCodes.Callvirt, Method(ts.String, "StartsWith", ts.Boolean, true, ts.String, Type("System.StringComparison", value: true)));
            il.Emit(OpCodes.Brfalse, original);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Call, Method(unityObject, "Destroy", ts.Void, false, unityObject));
            il.Emit(OpCodes.Ret);
            il.Append(original);
            il.Emit(OpCodes.Call, originalRelease);
            il.Emit(OpCodes.Ret);
            foreach (var instruction in releases) instruction.Operand = release;
        }
        PortraitAspectPatch.Apply(module, manager, resources);
    }
}
