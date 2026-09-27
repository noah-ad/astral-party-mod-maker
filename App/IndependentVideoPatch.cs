using Mono.Cecil;
using Mono.Cecil.Cil;

namespace JixModMaker;

public static class IndependentVideoPatch
{
    public const string Mode = "independent-file-v1";
    public const string Folder = "JixVideos";
    public const string KeyPrefix = "JixVideo_";
    public const string PersistentFolder = "/com.unity.addressables/AssetBundles/" + Folder + "/";

    public static string VideoKey(string texture)
    {
        if (!AnimatedPortraitPatch.IsSupportedTarget(texture) || texture.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Unsupported independent video target: " + texture);
        return KeyPrefix + texture;
    }

    public static string RelativeFile(string texture) => Folder + "/" + VideoKey(texture) + ".usm";

    public static byte[] PatchAssembly(byte[] original, IReadOnlyCollection<string> textures,
        IReadOnlyDictionary<string, byte[]> dependencies = null)
    {
        var targets = textures.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (targets.Length == 0 || targets.Length != textures.Count) throw new ArgumentException("Invalid target list");
        foreach (string target in targets) VideoKey(target);
        using var resolver = new AnimatedPortraitPatch.BundleAssemblyResolver(dependencies);
        using var module = ModuleDefinition.ReadModule(new MemoryStream(original, false), new ReaderParameters { AssemblyResolver = resolver });
        if (module.Assembly.Name.HasPublicKey || AllTypes(module.Types).Any(t => t.Namespace == "Jix.DynamicPortrait" ||
                t.Methods.Any(m => m.Name.StartsWith("Jix", StringComparison.Ordinal))))
            throw new InvalidOperationException("独立视频必须从首次备份的未打补丁程序集构建，请先还原旧动态替换。");

        var manager = RequireType(module, "UI.CriMovieManager");
        var controller = RequireType(module, "UI.CriManaMovieControllerForGGraph");
        var load = manager.Methods.Single(m => m.Name == "Load" && m.HasBody && m.Parameters.Count == 1);
        var setAsset = controller.Methods.Single(m => m.Name == "SetAsset" && m.HasBody && m.Parameters.Count == 2);
        if (load.ReturnType.FullName != "Cysharp.Threading.Tasks.UniTask`1<CriWare.Assets.CriManaUsmAsset>")
            throw new InvalidOperationException("视频加载接口已变化，已停止生成补丁。");

        var equality = Method(module.TypeSystem.String, "op_Equality", module.TypeSystem.Boolean, false,
            module.TypeSystem.String, module.TypeSystem.String);
        var concat = Method(module.TypeSystem.String, "Concat", module.TypeSystem.String, false,
            module.TypeSystem.String, module.TypeSystem.String);
        var fileType = FindType(module, "System.IO", "File", module.TypeSystem.CoreLibrary);
        var exists = Method(fileType, "Exists", module.TypeSystem.Boolean, false, module.TypeSystem.String);
        var application = FindType(module, "UnityEngine", "Application",
            module.AssemblyReferences.SingleOrDefault(a => a.Name == "UnityEngine.CoreModule"));
        var persistentPath = Method(application, "get_persistentDataPath", module.TypeSystem.String, false);

        var isKey = Helper(manager, "JixIsIndependentVideo", module.TypeSystem.Boolean, module.TypeSystem.String);
        var il = isKey.Body.GetILProcessor();
        foreach (string target in targets)
        {
            var next = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, VideoKey(target));
            il.Emit(OpCodes.Call, equality);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldc_I4_1);
            il.Emit(OpCodes.Ret);
            il.Append(next);
        }
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ret);

        var path = Helper(manager, "JixIndependentVideoPath", module.TypeSystem.String, module.TypeSystem.String);
        il = path.Body.GetILProcessor();
        il.Emit(OpCodes.Call, persistentPath);
        il.Emit(OpCodes.Ldstr, PersistentFolder);
        il.Emit(OpCodes.Call, concat);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, concat);
        il.Emit(OpCodes.Ldstr, ".usm");
        il.Emit(OpCodes.Call, concat);
        il.Emit(OpCodes.Ret);

        var lookup = Helper(manager, "JixLookupIndependentVideo", module.TypeSystem.String, module.TypeSystem.String);
        lookup.Body.InitLocals = true;
        var result = new VariableDefinition(module.TypeSystem.String);
        lookup.Body.Variables.Add(result);
        il = lookup.Body.GetILProcessor();
        var tryStart = Instruction.Create(OpCodes.Nop);
        var finish = Instruction.Create(OpCodes.Ldloc, result);
        var missing = Instruction.Create(OpCodes.Leave, finish);
        il.Append(tryStart);
        // A reversible kill switch is checked only when artwork is requested, never during startup.
        il.Emit(OpCodes.Call, persistentPath);
        il.Emit(OpCodes.Ldstr, PersistentFolder + "disabled");
        il.Emit(OpCodes.Call, concat);
        il.Emit(OpCodes.Call, exists);
        il.Emit(OpCodes.Brtrue, missing);
        foreach (string target in targets)
        {
            var next = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, target);
            il.Emit(OpCodes.Call, equality);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldstr, VideoKey(target));
            il.Emit(OpCodes.Call, path);
            il.Emit(OpCodes.Call, exists);
            il.Emit(OpCodes.Brfalse, missing);
            il.Emit(OpCodes.Ldstr, VideoKey(target));
            il.Emit(OpCodes.Stloc, result);
            il.Emit(OpCodes.Leave, finish);
            il.Append(next);
        }
        il.Append(missing);
        var handler = Instruction.Create(OpCodes.Pop);
        il.Append(handler);
        il.Emit(OpCodes.Leave, finish);
        il.Append(finish);
        il.Emit(OpCodes.Ret);
        lookup.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
        {
            CatchType = FindType(module, "System", "Exception", module.TypeSystem.CoreLibrary),
            TryStart = tryStart, TryEnd = handler, HandlerStart = handler, HandlerEnd = finish
        });

        // An empty completed UniTask is sufficient: our controller reads the USM directly.
        // Official keys still execute the original Addressables loader byte-for-byte.
        load.Body.InitLocals = true;
        var emptyTask = new VariableDefinition(load.ReturnType);
        load.Body.Variables.Add(emptyTask);
        var originalEntry = load.Body.Instructions[0];
        Prepend(load, originalEntry, new[]
        {
            Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Call, isKey),
            Instruction.Create(OpCodes.Brfalse, originalEntry), Instruction.Create(OpCodes.Ldloca, emptyTask),
            Instruction.Create(OpCodes.Initobj, load.ReturnType), Instruction.Create(OpCodes.Ldloc, emptyTask), Instruction.Create(OpCodes.Ret)
        });

        var nativeSetAsset = setAsset.Body.Instructions.Single(i => i.Operand is MethodReference m &&
            m.DeclaringType.FullName == "CriWare.Assets.CriManaPlayerExtentionForAsset" && m.Name == "SetAsset");
        var native = (MethodReference)nativeSetAsset.Operand;
        var player = native.Parameters[0].ParameterType;
        var getPlayer = (MethodReference)setAsset.Body.Instructions.First(i => i.Operand is MethodReference m && m.Name == "get_player").Operand;
        var binder = FindType(module, "CriWare", "CriFsBinder", player.Scope);
        var setFile = Method(player, "SetFile", module.TypeSystem.Boolean, true,
            binder, module.TypeSystem.String, native.Parameters[2].ParameterType);
        var loop = Method(player, "Loop", module.TypeSystem.Void, true, module.TypeSystem.Boolean);
        var additive = Method(player, "set_additiveMode", module.TypeSystem.Void, true, module.TypeSystem.Boolean);
        var start = nativeSetAsset.Previous?.Previous?.Previous?.Previous;
        if (start?.OpCode != OpCodes.Ldarg_0 || start.Next?.Operand != getPlayer)
            throw new InvalidOperationException("视频播放器初始化结构已变化，已停止生成补丁。");
        var direct = new[]
        {
            Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Call, isKey), Instruction.Create(OpCodes.Brfalse, start),
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, getPlayer), Instruction.Create(OpCodes.Ldnull),
            Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Call, path), Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Callvirt, setFile), Instruction.Create(OpCodes.Pop),
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, getPlayer), Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Callvirt, loop),
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Call, getPlayer), Instruction.Create(OpCodes.Ldc_I4_0),
            Instruction.Create(OpCodes.Callvirt, additive), Instruction.Create(OpCodes.Ret)
        };
        foreach (var instruction in setAsset.Body.Instructions.Where(i => ReferenceEquals(i.Operand, start))) instruction.Operand = direct[0];
        Prepend(setAsset, start, direct);

        if (targets.Any(AnimatedPortraitPatch.IsCharacterPortrait))
            AnimatedPortraitPatch.PatchCharacterPortrait(module, null, null, lookup);
        if (targets.Any(AnimatedPortraitPatch.IsCardArtwork))
            AnimatedPortraitPatch.PatchCardArtwork(module, null, null, lookup, isKey);

        using var output = new MemoryStream();
        module.Write(output);
        return output.ToArray();
    }

    private static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> types) =>
        types.SelectMany(t => new[] { t }.Concat(AllTypes(t.NestedTypes)));

    private static TypeDefinition RequireType(ModuleDefinition module, string name) => module.GetType(name) ??
        throw new InvalidOperationException("Missing game type: " + name);

    private static TypeReference FindType(ModuleDefinition module, string ns, string name, IMetadataScope scope) =>
        module.GetType(ns + "." + name) ?? module.GetTypeReferences().FirstOrDefault(t => t.FullName == ns + "." + name) ??
        (scope == null ? throw new InvalidOperationException("Missing game assembly for " + name) : new TypeReference(ns, name, module, scope));

    private static MethodReference Method(TypeReference owner, string name, TypeReference returns, bool instance, params TypeReference[] parameters)
    {
        var method = new MethodReference(name, returns, owner) { HasThis = instance };
        foreach (var parameter in parameters) method.Parameters.Add(new ParameterDefinition(parameter));
        return method;
    }

    private static MethodDefinition Helper(TypeDefinition owner, string name, TypeReference returns, params TypeReference[] parameters)
    {
        var method = new MethodDefinition(name, MethodAttributes.Assembly | MethodAttributes.Static, returns);
        foreach (var parameter in parameters) method.Parameters.Add(new ParameterDefinition(parameter));
        owner.Methods.Add(method);
        return method;
    }

    private static void Prepend(MethodDefinition method, Instruction before, IEnumerable<Instruction> instructions)
    {
        var il = method.Body.GetILProcessor();
        foreach (var instruction in instructions) il.InsertBefore(before, instruction);
    }
}
