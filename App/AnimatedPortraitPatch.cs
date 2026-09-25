using AssetsTools.NET;
using AssetsTools.NET.Extra;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text;

namespace JixModMaker;

// Uses one existing official video asset. It never embeds video bytes in the hot-update DLL.
public static class AnimatedPortraitPatch
{
    public const string VideoSlot = "VHandCard_13021002";
    private const string PortraitHelperName = "JixMapAnimatedPortrait";
    private const string CardMapHelperName = "JixMapAnimatedCard";
    private const string CardLayerHelperName = "JixCardVideoLayer";
    private const string CardLayoutHelperName = "JixCardVideoLayout";
    private const string CardPrepareHelperName = "JixPrepareAnimatedCard";
    private const string GraphPrepareHelperName = "JixPrepareCardGraph";
    private const string GraphAttachHelperName = "JixCanAttachCardGraph";

    public sealed record Request(string Root, string RuntimeBundle, string VideoBundle, string TextureName, string UsmFile, string Destination);
    public sealed record Result(string ReplacementZip, string RestoreZip, string VideoKey, string Platform);

    public static bool IsCharacterPortrait(string name) => name != null &&
        name.StartsWith("UT_Hero_Card_", StringComparison.Ordinal) &&
        name.Length > "UT_Hero_Card_".Length &&
        name["UT_Hero_Card_".Length] is >= '0' and <= '9';

    public static bool IsCardArtwork(string name) => HasNumericSuffix(name, "UT_HandCard_") ||
        HasNumericSuffix(name, "UT_Event_") || HasNumericSuffix(name, "UT_MapEvent_");

    public static bool IsSupportedTarget(string name) => IsCharacterPortrait(name) || IsCardArtwork(name);

    private static bool HasNumericSuffix(string name, string prefix) => name != null &&
        name.StartsWith(prefix, StringComparison.Ordinal) && name.Length > prefix.Length &&
        name[prefix.Length] is >= '0' and <= '9';

    public static byte[] PatchAssembly(byte[] original, string texture, string videoKey,
        IReadOnlyDictionary<string, byte[]> dependencies = null)
    {
        if (!IsSupportedTarget(texture)) throw new ArgumentException("动态替换仅支持角色立绘、手牌和事件卡");
        if (!string.Equals(videoKey, VideoSlot, StringComparison.Ordinal))
            throw new InvalidDataException("视频包不是已验证的原生异画槽位：" + VideoSlot);

        using var input = new MemoryStream(original, false);
        using var resolver = new BundleAssemblyResolver(dependencies);
        using var module = ModuleDefinition.ReadModule(input, new ReaderParameters { AssemblyResolver = resolver });
        if (module.Assembly.Name.HasPublicKey) throw new InvalidOperationException("不支持修改带签名的程序集");
        if (module.Types.Any(t => t.Namespace == "Jix.DynamicPortrait") ||
            module.Types.SelectMany(t => t.Methods).Any(m => m.Name == "JixLoadIndependentPortrait"))
            throw new InvalidOperationException("检测到独立动态资源补丁。请先恢复该版本的首次备份，再使用原生视频槽位模式。");

        var existingPortrait = module.Types.SelectMany(t => t.Methods).SingleOrDefault(m => m.Name == PortraitHelperName);
        var existingCard = module.Types.SelectMany(t => t.Methods).SingleOrDefault(m => m.Name == CardMapHelperName);
        var expected = IsCharacterPortrait(texture) ? existingPortrait : existingCard;
        if (existingPortrait != null || existingCard != null)
        {
            var strings = expected?.Body?.Instructions.Where(i => i.OpCode == OpCodes.Ldstr)
                .Select(i => i.Operand as string).ToArray();
            if ((existingPortrait == null || existingCard == null) && strings?.SequenceEqual(new[] { texture, videoKey }) == true)
                return original.ToArray();
            throw new InvalidOperationException("原生视频槽位已用于其他动态资源，请先恢复原资源再替换。");
        }

        if (IsCharacterPortrait(texture)) PatchCharacterPortrait(module, texture, videoKey);
        else PatchCardArtwork(module, texture, videoKey);

        using var output = new MemoryStream();
        module.Write(output);
        return output.ToArray();
    }

    private static void PatchCharacterPortrait(ModuleDefinition module, string texture, string videoKey)
    {

        var config = module.Types.SingleOrDefault(t => t.FullName == "SkinStandingPaintingConfigureItem") ??
            throw new InvalidOperationException("没有找到立绘配置类型，游戏版本不兼容");
        var methods = new[] { "GetCharacter", "GetCharacterInGame" }
            .Select(name => config.Methods.SingleOrDefault(m => m.Name == name && m.Parameters.Count == 0 && m.HasBody) ??
                throw new InvalidOperationException("没有找到 " + name + "，游戏版本不兼容"))
            .ToArray();
        if (methods.Any(m => m.ReturnType.FullName != "System.ValueTuple`2<System.String,System.Boolean>"))
            throw new InvalidOperationException("立绘方法签名已改变，停止导出");
        var constructors = methods.SelectMany(m => m.Body.Instructions)
            .Where(i => i.OpCode == OpCodes.Newobj && i.Operand is MethodReference constructor &&
                constructor.DeclaringType.FullName == methods[0].ReturnType.FullName && constructor.Parameters.Count == 2)
            .ToArray();
        if (constructors.Length == 0 || methods.Any(m => !m.Body.Instructions.Any(constructors.Contains)) ||
            constructors.Any(i => i.Previous?.OpCode != OpCodes.Ldc_I4_0))
            throw new InvalidOperationException("立绘逻辑与已验证版本不同，停止导出");

        var equality = new MethodReference("op_Equality", module.TypeSystem.Boolean, module.TypeSystem.String) { HasThis = false };
        equality.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        equality.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        var tupleConstructor = (MethodReference)constructors[0].Operand;
        var map = new MethodDefinition(PortraitHelperName, MethodAttributes.Private | MethodAttributes.Static, methods[0].ReturnType);
        map.Parameters.Add(new ParameterDefinition("key", ParameterAttributes.None, module.TypeSystem.String));
        map.Parameters.Add(new ParameterDefinition("isVideo", ParameterAttributes.None, module.TypeSystem.Boolean));
        config.Methods.Add(map);
        var il = map.Body.GetILProcessor();
        var unchanged = Instruction.Create(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldstr, texture);
        il.Emit(OpCodes.Call, equality);
        il.Emit(OpCodes.Brfalse, unchanged);
        il.Emit(OpCodes.Ldstr, videoKey);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newobj, tupleConstructor);
        il.Emit(OpCodes.Ret);
        il.Append(unchanged);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, tupleConstructor);
        il.Emit(OpCodes.Ret);
        foreach (var instruction in constructors)
        {
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = map;
        }
    }

    private static void PatchCardArtwork(ModuleDefinition module, string texture, string videoKey)
    {
        TypeDefinition RequireType(string fullName) => module.GetType(fullName) ??
            throw new InvalidOperationException("没有找到 " + fullName + "，游戏版本不兼容");
        static FieldDefinition RequireField(TypeDefinition type, string name) => type.Fields.SingleOrDefault(f => f.Name == name) ??
            throw new InvalidOperationException("没有找到 " + type.FullName + "." + name + "，游戏版本不兼容");
        static MethodDefinition RequireMethod(TypeDefinition type, string name, Func<MethodDefinition, bool> match) =>
            type.Methods.SingleOrDefault(m => m.Name == name && match(m)) ??
            throw new InvalidOperationException("没有找到 " + type.FullName + "." + name + "，游戏版本不兼容");

        var card = RequireType("CardView");
        var common = RequireType("UI.CommonUIManager");
        var uiCard = RequireType("UI.UICom_Card");
        var movieManager = RequireType("UI.CriMovieManager");
        var movieBridge = RequireType("UI.CriManaMovieBridge");
        var graph = RequireType("FairyGUI.GGraph");
        var gObject = RequireType("FairyGUI.GObject");
        var gearDisplay = RequireType("FairyGUI.GearDisplay2");
        var controller = RequireType("FairyGUI.Controller");
        var keyField = RequireField(card, "Key");
        var isVideoField = RequireField(card, "IsVideo");
        var cardTypeField = RequireField(card, "CardType");
        var frontGraphField = RequireField(uiCard, "video_FrontCard");
        var fullGraphField = RequireField(uiCard, "video_FullCard");
        var frontStateField = RequireField(uiCard, "frontState");
        var graphVideoKey = RequireField(graph, "VideoKey");
        var bridgeVideoKey = RequireField(movieBridge, "_videoKey");
        var renderer = RequireMethod(common, "RendererCardContent", method => method.HasBody && method.Parameters.Count >= 2 &&
            method.Parameters[0].ParameterType.FullName == uiCard.FullName && method.Parameters[1].ParameterType.FullName == card.FullName);
        var addVideoGraph = RequireMethod(movieBridge, "AddVideoGraph", method => method.HasBody && method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType.FullName == graph.FullName);
        var cardConstructor = RequireMethod(card, ".ctor", method => !method.IsStatic && method.Parameters.Count == 0);
        var getGear = RequireMethod(gObject, "GetGear", method => method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType.MetadataType == MetadataType.Int32);
        var handleController = RequireMethod(gObject, "HandleControllerChanged", method => method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType.FullName == controller.FullName);
        var getPageId = RequireMethod(controller, "GetPageId", method => method.Parameters.Count == 1 &&
            method.Parameters[0].ParameterType.MetadataType == MetadataType.Int32);
        var pages = gearDisplay.Properties.SingleOrDefault(p => p.Name == "pages" && p.GetMethod != null && p.SetMethod != null) ??
            throw new InvalidOperationException("没有找到卡牌视频图层页配置，游戏版本不兼容");
        var visible = gObject.Properties.SingleOrDefault(p => p.Name == "visible" && p.SetMethod != null) ??
            throw new InvalidOperationException("没有找到卡牌视频图层显示状态，游戏版本不兼容");

        if (uiCard.Fields.Any(f => f.Name is "JixOriginalVideoPages" or "JixVideoPagesOverridden") ||
            graph.Fields.Any(f => f.Name is "JixCardRequestTracked" or "JixCardRequestedKey"))
            throw new InvalidOperationException("检测到不完整的旧版动态卡牌补丁，请先恢复原资源。");

        var equality = new MethodReference("op_Equality", module.TypeSystem.Boolean, module.TypeSystem.String) { HasThis = false };
        equality.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
        equality.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

        var originalPages = new FieldDefinition("JixOriginalVideoPages", FieldAttributes.Assembly, new ArrayType(module.TypeSystem.String));
        var pagesOverridden = new FieldDefinition("JixVideoPagesOverridden", FieldAttributes.Assembly, module.TypeSystem.Boolean);
        uiCard.Fields.Add(originalPages);
        uiCard.Fields.Add(pagesOverridden);
        var requestTracked = new FieldDefinition("JixCardRequestTracked", FieldAttributes.Assembly, module.TypeSystem.Boolean);
        var requestedKey = new FieldDefinition("JixCardRequestedKey", FieldAttributes.Assembly, module.TypeSystem.String);
        graph.Fields.Add(requestTracked);
        graph.Fields.Add(requestedKey);

        var map = new MethodDefinition(CardMapHelperName, MethodAttributes.Assembly | MethodAttributes.Static, card);
        map.Parameters.Add(new ParameterDefinition("card", ParameterAttributes.None, card));
        map.Body.InitLocals = true;
        var clone = new VariableDefinition(card);
        map.Body.Variables.Add(clone);
        common.Methods.Add(map);
        var il = map.Body.GetILProcessor();
        var unchanged = Instruction.Create(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, unchanged);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, isVideoField);
        il.Emit(OpCodes.Brtrue, unchanged);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, keyField);
        il.Emit(OpCodes.Ldstr, texture);
        il.Emit(OpCodes.Call, equality);
        il.Emit(OpCodes.Brfalse, unchanged);
        il.Emit(OpCodes.Newobj, cardConstructor);
        il.Emit(OpCodes.Stloc, clone);
        foreach (var field in card.Fields.Where(f => !f.IsStatic && (f.IsPublic || f.IsAssembly)))
        {
            il.Emit(OpCodes.Ldloc, clone);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, field);
            il.Emit(OpCodes.Stfld, field);
        }
        il.Emit(OpCodes.Ldloc, clone);
        il.Emit(OpCodes.Ldstr, videoKey);
        il.Emit(OpCodes.Stfld, keyField);
        il.Emit(OpCodes.Ldloc, clone);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, isVideoField);
        il.Emit(OpCodes.Ldloc, clone);
        il.Emit(OpCodes.Ret);
        il.Append(unchanged);
        il.Emit(OpCodes.Ret);

        var layer = new MethodDefinition(CardLayerHelperName, MethodAttributes.Assembly | MethodAttributes.Static, module.TypeSystem.Int32);
        layer.Parameters.Add(new ParameterDefinition("card", ParameterAttributes.None, card));
        common.Methods.Add(layer);
        il = layer.Body.GetILProcessor();
        var originalLayer = Instruction.Create(OpCodes.Ldarg_0);
        var zeroLayer = Instruction.Create(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, zeroLayer);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, cardTypeField);
        il.Emit(OpCodes.Brtrue, originalLayer);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, isVideoField);
        il.Emit(OpCodes.Brfalse, originalLayer);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, keyField);
        il.Emit(OpCodes.Ldstr, videoKey);
        il.Emit(OpCodes.Call, equality);
        il.Emit(OpCodes.Brfalse, originalLayer);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ret);
        il.Append(originalLayer);
        il.Emit(OpCodes.Ldfld, cardTypeField);
        il.Emit(OpCodes.Ret);
        il.Append(zeroLayer);
        il.Emit(OpCodes.Ret);

        var prepareGraph = new MethodDefinition(GraphPrepareHelperName, MethodAttributes.Assembly | MethodAttributes.Static, module.TypeSystem.Void);
        prepareGraph.Parameters.Add(new ParameterDefinition("key", ParameterAttributes.None, module.TypeSystem.String));
        prepareGraph.Parameters.Add(new ParameterDefinition("graph", ParameterAttributes.None, graph));
        movieManager.Methods.Add(prepareGraph);
        il = prepareGraph.Body.GetILProcessor();
        var trackRequest = Instruction.Create(OpCodes.Ldarg_1);
        var prepareDone = Instruction.Create(OpCodes.Ret);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, prepareDone);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, graphVideoKey);
        il.Emit(OpCodes.Call, equality);
        il.Emit(OpCodes.Brtrue, trackRequest);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, graphVideoKey);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, visible.SetMethod);
        il.Append(trackRequest);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, requestTracked);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Stfld, requestedKey);
        il.Append(prepareDone);

        var canAttach = new MethodDefinition(GraphAttachHelperName, MethodAttributes.Assembly | MethodAttributes.Static, module.TypeSystem.Boolean);
        canAttach.Parameters.Add(new ParameterDefinition("key", ParameterAttributes.None, module.TypeSystem.String));
        canAttach.Parameters.Add(new ParameterDefinition("graph", ParameterAttributes.None, graph));
        movieManager.Methods.Add(canAttach);
        il = canAttach.Body.GetILProcessor();
        var allow = Instruction.Create(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, allow);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, requestTracked);
        il.Emit(OpCodes.Brfalse, allow);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, requestedKey);
        il.Emit(OpCodes.Call, equality);
        il.Emit(OpCodes.Ret);
        il.Append(allow);
        il.Emit(OpCodes.Ret);

        var addIl = addVideoGraph.Body.GetILProcessor();
        var addEntry = addVideoGraph.Body.Instructions[0];
        foreach (var instruction in new[]
        {
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldfld, bridgeVideoKey),
            Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Call, canAttach),
            Instruction.Create(OpCodes.Brtrue, addEntry), Instruction.Create(OpCodes.Ret)
        }) addIl.InsertBefore(addEntry, instruction);

        var layout = new MethodDefinition(CardLayoutHelperName, MethodAttributes.Assembly | MethodAttributes.Static, module.TypeSystem.Void);
        layout.Parameters.Add(new ParameterDefinition("view", ParameterAttributes.None, uiCard));
        layout.Parameters.Add(new ParameterDefinition("card", ParameterAttributes.None, card));
        layout.Body.InitLocals = true;
        var display = new VariableDefinition(gearDisplay);
        layout.Body.Variables.Add(display);
        common.Methods.Add(layout);
        il = layout.Body.GetILProcessor();
        var restorePages = Instruction.Create(OpCodes.Nop);
        var applyController = Instruction.Create(OpCodes.Ldarg_0);
        var layoutDone = Instruction.Create(OpCodes.Ret);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, frontGraphField);
        il.Emit(OpCodes.Ldc_I4_8);
        il.Emit(OpCodes.Callvirt, getGear);
        il.Emit(OpCodes.Isinst, gearDisplay);
        il.Emit(OpCodes.Stloc, display);
        il.Emit(OpCodes.Ldloc, display);
        il.Emit(OpCodes.Brfalse, layoutDone);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, cardTypeField);
        il.Emit(OpCodes.Brtrue, restorePages);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, layer);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Bne_Un, restorePages);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, pagesOverridden);
        il.Emit(OpCodes.Brtrue, layoutDone);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, display);
        il.Emit(OpCodes.Callvirt, pages.GetMethod);
        il.Emit(OpCodes.Stfld, originalPages);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Stfld, pagesOverridden);
        il.Emit(OpCodes.Ldloc, display);
        il.Emit(OpCodes.Ldc_I4_1);
        il.Emit(OpCodes.Newarr, module.TypeSystem.String);
        il.Emit(OpCodes.Dup);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, frontStateField);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, getPageId);
        il.Emit(OpCodes.Stelem_Ref);
        il.Emit(OpCodes.Callvirt, pages.SetMethod);
        il.Emit(OpCodes.Br, applyController);
        il.Append(restorePages);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, pagesOverridden);
        il.Emit(OpCodes.Brfalse, layoutDone);
        il.Emit(OpCodes.Ldloc, display);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, originalPages);
        il.Emit(OpCodes.Callvirt, pages.SetMethod);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Stfld, originalPages);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Stfld, pagesOverridden);
        il.Append(applyController);
        il.Emit(OpCodes.Ldfld, frontGraphField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, frontStateField);
        il.Emit(OpCodes.Callvirt, handleController);
        il.Append(layoutDone);

        var prepare = new MethodDefinition(CardPrepareHelperName, MethodAttributes.Assembly | MethodAttributes.Static, card);
        prepare.Parameters.Add(new ParameterDefinition("view", ParameterAttributes.None, uiCard));
        prepare.Parameters.Add(new ParameterDefinition("card", ParameterAttributes.None, card));
        common.Methods.Add(prepare);
        il = prepare.Body.GetILProcessor();
        var returnCard = Instruction.Create(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, map);
        il.Emit(OpCodes.Starg_S, prepare.Parameters[1]);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse, returnCard);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, returnCard);
        EmitGraphPreparation(1, frontGraphField);
        EmitGraphPreparation(2, fullGraphField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, layout);
        il.Append(returnCard);
        il.Emit(OpCodes.Ret);

        void EmitGraphPreparation(int targetLayer, FieldDefinition graphField)
        {
            var noKey = Instruction.Create(OpCodes.Ldnull);
            var keyReady = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldfld, isVideoField);
            il.Emit(OpCodes.Brfalse, noKey);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, layer);
            il.Emit(targetLayer == 1 ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_2);
            il.Emit(OpCodes.Bne_Un, noKey);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldfld, keyField);
            il.Emit(OpCodes.Br, keyReady);
            il.Append(noKey);
            il.Append(keyReady);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldfld, graphField);
            il.Emit(OpCodes.Call, prepareGraph);
        }

        var cardTypeLoads = renderer.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ldfld &&
            instruction.Operand is FieldReference field && field.FullName == cardTypeField.FullName &&
            instruction.Next?.OpCode is var next && (next == OpCodes.Ldc_I4_1 || next == OpCodes.Ldc_I4_2)).ToArray();
        if (cardTypeLoads.Length != 2)
            throw new InvalidOperationException("卡牌视频分支与已验证版本不同，停止导出。");
        foreach (var instruction in cardTypeLoads)
        {
            instruction.OpCode = OpCodes.Call;
            instruction.Operand = layer;
        }
        var rendererIl = renderer.Body.GetILProcessor();
        var rendererEntry = renderer.Body.Instructions[0];
        foreach (var instruction in new[]
        {
            Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Ldarg_1),
            Instruction.Create(OpCodes.Call, prepare), Instruction.Create(OpCodes.Starg_S, renderer.Parameters[1])
        }) rendererIl.InsertBefore(rendererEntry, instruction);
    }

    public static (string Runtime, string Video) FindBundles(string root, CancellationToken cancellationToken,
        IProgress<int> progress = null)
    {
        string runtime = null;
        string video = null;
        var manager = Manager();
        int scanned = 0;
        try
        {
            var files = ResourceLocator.EnumerateResourceFiles(root)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.Length >= 4_000_000)
                .ThenByDescending(file => file.LastWriteTimeUtc)
                .ToList();
            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var bundle = manager.LoadBundleFile(path.FullName);
                    var file = manager.LoadAssetsFileFromBundle(bundle, 0, false);
                    if (!file.file.Metadata.UnityVersion.StartsWith("2021.", StringComparison.Ordinal)) continue;
                    foreach (var info in file.file.AssetInfos)
                    {
                        bool textAsset = info.TypeId == (int)AssetClassID.TextAsset;
                        if (!textAsset && info.TypeId != (int)AssetClassID.MonoBehaviour) continue;
                        var reader = file.file.Reader;
                        reader.Position = info.GetAbsoluteByteStart(file.file) + (textAsset ? 0 : 28);
                        int length = reader.ReadInt32();
                        if (length < 0 || length > 256 || length + (textAsset ? 4 : 32) > info.ByteSize) continue;
                        string name = Encoding.UTF8.GetString(reader.ReadBytes(length));
                        if (textAsset && name == "AstralParty.Runtime.dll") runtime ??= path.FullName;
                        if (!textAsset && name == VideoSlot) video ??= path.FullName;
                    }
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException) { }
                finally { manager.UnloadAll(); }
                if (++scanned % 25 == 0) progress?.Report(scanned);
                if (runtime != null && video != null) break;
            }
            return (runtime, video);
        }
        finally { manager.UnloadAll(); }
    }

    public static Result Export(Request request)
    {
        if (!IsSupportedTarget(request.TextureName)) throw new ArgumentException("动态替换仅支持角色立绘、手牌和事件卡");
        string root = Path.GetFullPath(request.Root);
        string runtimeRelative = Relative(request.RuntimeBundle);
        string videoRelative = Relative(request.VideoBundle);
        if (string.Equals(runtimeRelative, videoRelative, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("程序集资源包和视频资源包不能相同");
        string destination = Path.GetFullPath(request.Destination);
        string restore = Path.Combine(Path.GetDirectoryName(destination)!, Path.GetFileNameWithoutExtension(destination) + ".restore.zip");
        if (File.Exists(destination) || File.Exists(restore)) throw new IOException("输出或恢复 ZIP 已存在，请选择新文件名");
        if (new FileInfo(request.UsmFile).Length > 256L * 1024 * 1024)
            throw new InvalidDataException("暂不接受大于 256 MB 的单段视频");
        byte[] usm = File.ReadAllBytes(request.UsmFile);
        if (usm.Length < 32 || Encoding.ASCII.GetString(usm, 0, 4) != "CRID")
            throw new InvalidDataException("需要真正的 USM 视频，不能将 GIF / MP4 改名后导入");
        PortraitMovieMetadata.Read(request.UsmFile).Validate(usm);

        string temporary = Path.Combine(Path.GetTempPath(), "JixAnimatedPortrait-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            string runtimeOut = Path.Combine(temporary, runtimeRelative);
            string videoOut = Path.Combine(temporary, videoRelative);
            byte[] expectedAssembly = null;
            string expectedVideoKey = ReadVideoKey(request.VideoBundle);
            var runtimePlatform = Rewrite(request.RuntimeBundle, runtimeOut, (manager, file) =>
            {
                RejectMalformedTypeTree(file.file);
                var matches = file.file.GetAssetsOfType(AssetClassID.TextAsset)
                    .Where(info => manager.GetBaseField(file, info)["m_Name"].AsString == "AstralParty.Runtime.dll").ToArray();
                if (matches.Length != 1) throw new InvalidDataException("所选资源包不包含唯一的 AstralParty.Runtime.dll");
                var info = matches[0];
                var reader = file.file.Reader;
                reader.Position = info.GetAbsoluteByteStart(file.file);
                reader.ReadCountStringInt32();
                reader.Align();
                long contentStart = reader.Position;
                byte[] original = reader.ReadBytes(reader.ReadInt32());
                var dependencies = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                foreach (var dependency in file.file.GetAssetsOfType(AssetClassID.TextAsset))
                {
                    reader.Position = dependency.GetAbsoluteByteStart(file.file);
                    string name = reader.ReadCountStringInt32();
                    reader.Align();
                    int size = reader.ReadInt32();
                    if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        dependencies[Path.GetFileNameWithoutExtension(name)] = reader.ReadBytes(size);
                }
                byte[] patched = PatchAssembly(original, request.TextureName, expectedVideoKey, dependencies);
                expectedAssembly = patched;
                reader.Position = info.GetAbsoluteByteStart(file.file);
                byte[] prefix = reader.ReadBytes(checked((int)(contentStart - reader.Position)));
                using var stream = new MemoryStream();
                using (var writer = new AssetsFileWriter(stream))
                {
                    writer.BigEndian = reader.BigEndian;
                    writer.Write(prefix);
                    writer.Write(patched.Length);
                    writer.Write(patched);
                    writer.Align();
                }
                return new AssetsReplacerFromMemory(info.PathId, info.TypeId, file.file.GetScriptIndex(info), stream.ToArray());
            });

            var videoPlatform = Rewrite(request.VideoBundle, videoOut, (manager, file) =>
            {
                RejectMalformedTypeTree(file.file);
                var found = FindVideo(manager, file);
                var reader = file.file.Reader;
                reader.Position = found.Info.GetAbsoluteByteStart(file.file);
                byte[] bytes = reader.ReadBytes(checked((int)found.Info.ByteSize));
                byte[] oldVideo = found.Data.AsByteArray;
                int dataOffset = bytes.AsSpan().IndexOf(oldVideo);
                if (dataOffset < 4 || bytes.AsSpan(dataOffset + 1).IndexOf(oldVideo) >= 0)
                    throw new InvalidDataException("无法唯一定位内嵌视频数据");
                using (var oldReader = new AssetsFileReader(new MemoryStream(bytes, false)) { BigEndian = reader.BigEndian })
                {
                    oldReader.Position = dataOffset - 4;
                    if (oldReader.ReadInt32() != oldVideo.Length) throw new InvalidDataException("内嵌视频长度字段不匹配");
                }
                int oldEnd = (dataOffset + oldVideo.Length + 3) & ~3;
                if (oldEnd > bytes.Length) throw new InvalidDataException("内嵌视频超出对象边界");

                long loopOffset;
                using (var layout = new MemoryStream())
                {
                    using var writer = new AssetsFileWriter(layout) { BigEndian = reader.BigEndian };
                    foreach (var child in found.Field.Children.TakeWhile(child => child.FieldName != "assetInfo")) child.Write(writer);
                    loopOffset = layout.Position;
                }
                if (loopOffset >= dataOffset - 4 || bytes[loopOffset] != found.Field["assetInfo"]["loop"].AsByte)
                    throw new InvalidDataException("循环标记布局与预期不一致");
                bytes[loopOffset] = 1;
                using var replacement = new MemoryStream();
                using (var writer = new AssetsFileWriter(replacement) { BigEndian = reader.BigEndian })
                {
                    writer.Write(bytes.AsSpan(0, dataOffset - 4).ToArray());
                    writer.Write(usm.Length);
                    writer.Write(usm);
                    writer.Align();
                    writer.Write(bytes.AsSpan(oldEnd).ToArray());
                }
                return new AssetsReplacerFromMemory(found.Info.PathId, found.Info.TypeId,
                    file.file.GetScriptIndex(found.Info), replacement.ToArray());
            });

            if (runtimePlatform != videoPlatform)
                throw new InvalidOperationException("视频和程序集的平台不一致，不能混用电脑 / 手机资源");
            if (runtimePlatform is not "13" and not "19")
                throw new InvalidOperationException("动态替换仅接受 Android 或 Windows 64 位资源");
            VerifyRebuiltBundles(runtimeOut, videoOut, expectedAssembly, usm, expectedVideoKey);
            VerifyTypeLayout(request.RuntimeBundle, runtimeOut);
            VerifyTypeLayout(request.VideoBundle, videoOut);
            ReplacementZip.Export(root, new[] { request.RuntimeBundle, request.VideoBundle }, restore);
            try { ReplacementZip.Export(temporary, new[] { runtimeOut, videoOut }, destination); }
            catch { File.Delete(restore); throw; }
            return new Result(destination, restore, expectedVideoKey, runtimePlatform == "13" ? "Android" : "Windows 64 位");
        }
        finally { Directory.Delete(temporary, true); }

        string Relative(string path)
        {
            string relative = Path.GetRelativePath(root, Path.GetFullPath(path));
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
                throw new InvalidOperationException("两个资源包必须位于同一个目标平台资源根目录内");
            return relative;
        }
    }

    private static void RejectMalformedTypeTree(AssetsFile file)
    {
        if (file.Metadata.TypeTreeEnabled && file.Metadata.TypeTreeTypes.Any(type =>
                type.TypeId == (int)AssetClassID.TextAsset && type.Nodes.Count == 0))
            throw new InvalidDataException("源程序集包含旧版损坏类型表，请使用游戏重置后的干净资源制作。");
    }

    private static void VerifyRebuiltBundles(string runtimePath, string videoPath, byte[] assembly, byte[] usm, string videoKey)
    {
        var manager = Manager();
        try
        {
            var runtime = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(runtimePath), 0, false);
            manager.LoadClassDatabaseFromPackage(runtime.file.Metadata.UnityVersion);
            var info = runtime.file.GetAssetsOfType(AssetClassID.TextAsset)
                .Single(item => manager.GetBaseField(runtime, item)["m_Name"].AsString == "AstralParty.Runtime.dll");
            var reader = runtime.file.Reader;
            reader.Position = info.GetAbsoluteByteStart(runtime.file);
            reader.ReadCountStringInt32();
            reader.Align();
            int length = reader.ReadInt32();
            if (length != assembly.Length || !reader.ReadBytes(length).AsSpan().SequenceEqual(assembly))
                throw new InvalidDataException("程序集封包回读不一致");

            var video = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(videoPath), 0, false);
            manager.LoadClassDatabaseFromPackage(video.file.Metadata.UnityVersion);
            var found = FindVideo(manager, video);
            if (found.Field["m_Name"].AsString != videoKey || found.Field["assetInfo"]["loop"].AsByte != 1 ||
                !found.Data.AsByteArray.AsSpan().SequenceEqual(usm))
                throw new InvalidDataException("视频封包回读不一致");
        }
        finally { manager.UnloadAll(); }
    }

    private static (AssetFileInfo Info, AssetTypeValueField Field, AssetTypeValueField Data) FindVideo(
        AssetsManager manager, AssetsFileInstance file)
    {
        var matches = new List<(AssetFileInfo, AssetTypeValueField, AssetTypeValueField)>();
        foreach (var info in file.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            var field = manager.GetBaseField(file, info);
            if (field["m_Name"].AsString != VideoSlot) continue;
            var implementation = field["references"].Value?.AsManagedReferencesRegistry?.references
                .SingleOrDefault(reference => reference.type.ClassName == "CriSerializedBytesAssetImpl");
            if (implementation == null) continue;
            var data = implementation.data["data"]["Array"];
            byte[] bytes = data.AsByteArray;
            if (bytes.Length >= 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "CRID")
                matches.Add((info, field, data));
        }
        if (matches.Count != 1)
            throw new InvalidDataException("资源包必须只包含一个 " + VideoSlot + " 内嵌 USM 视频");
        return matches[0];
    }

    private static string ReadVideoKey(string path)
    {
        var manager = Manager();
        try
        {
            var file = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(path), 0, false);
            manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
            return FindVideo(manager, file).Field["m_Name"].AsString;
        }
        finally { manager.UnloadAll(); }
    }

    private sealed class BundleAssemblyResolver : DefaultAssemblyResolver
    {
        private readonly IReadOnlyDictionary<string, byte[]> _dependencies;
        private readonly Dictionary<string, AssemblyDefinition> _loaded = new(StringComparer.OrdinalIgnoreCase);
        public BundleAssemblyResolver(IReadOnlyDictionary<string, byte[]> dependencies) => _dependencies = dependencies;
        public override AssemblyDefinition Resolve(AssemblyNameReference name)
        {
            if (_loaded.TryGetValue(name.Name, out var existing)) return existing;
            if (_dependencies != null && _dependencies.TryGetValue(name.Name, out var bytes))
            {
                var assembly = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes, false),
                    new ReaderParameters { AssemblyResolver = this });
                _loaded[name.Name] = assembly;
                return assembly;
            }
            return base.Resolve(name);
        }
        protected override void Dispose(bool disposing)
        {
            foreach (var assembly in _loaded.Values) assembly.Dispose();
            base.Dispose(disposing);
        }
    }

    private static AssetsManager Manager()
    {
        var manager = new AssetsManager();
        manager.LoadClassPackage(Path.Combine(AppContext.BaseDirectory, "classdata.tpk"));
        return manager;
    }

    public static void VerifyTypeLayout(string original, string rebuilt)
    {
        var manager = Manager();
        try
        {
            var before = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(original), 0, false).file;
            var after = manager.LoadAssetsFileFromBundle(manager.LoadBundleFile(rebuilt), 0, false).file;
            byte[] TypeBytes(AssetsFile file)
            {
                using var buffer = new MemoryStream();
                using var writer = new AssetsFileWriter(buffer);
                foreach (var type in file.Metadata.TypeTreeTypes.Concat(file.Metadata.RefTypes))
                    type.Write(writer, file.Header.Version, file.Metadata.TypeTreeEnabled);
                return buffer.ToArray();
            }
            if (before.Header.Version != after.Header.Version || before.Metadata.UnityVersion != after.Metadata.UnityVersion ||
                before.Metadata.TargetPlatform != after.Metadata.TargetPlatform || before.Metadata.TypeTreeEnabled != after.Metadata.TypeTreeEnabled ||
                before.Metadata.TypeTreeTypes.Count != after.Metadata.TypeTreeTypes.Count || before.Metadata.RefTypes.Count != after.Metadata.RefTypes.Count ||
                !TypeBytes(before).AsSpan().SequenceEqual(TypeBytes(after)) ||
                !before.AssetInfos.Select(info => (info.PathId, info.TypeIdOrIndex))
                    .SequenceEqual(after.AssetInfos.Select(info => (info.PathId, info.TypeIdOrIndex))))
                throw new InvalidDataException("封包类型结构发生变化，已阻止写入游戏。");
        }
        finally { manager.UnloadAll(); }
    }

    private static string Rewrite(string source, string destination, Func<AssetsManager, AssetsFileInstance, AssetsReplacer> change)
    {
        var manager = Manager();
        try
        {
            var bundle = manager.LoadBundleFile(source);
            var file = manager.LoadAssetsFileFromBundle(bundle, 0, false);
            manager.LoadClassDatabaseFromPackage(file.file.Metadata.UnityVersion);
            var replacement = change(manager, file);
            byte[] bytes;
            using (var buffer = new MemoryStream())
            {
                using var writer = new AssetsFileWriter(buffer);
                file.file.Write(writer, 0, new List<AssetsReplacer> { replacement }, null);
                bytes = buffer.ToArray();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using (var writer = new AssetsFileWriter(destination))
                bundle.file.Write(writer, new List<BundleReplacer>
                {
                    new BundleReplacerFromMemory(file.name, file.name, true, bytes, -1)
                });
            string infoPath = Path.Combine(Path.GetDirectoryName(source)!, "__info");
            if (Path.GetFileName(source) == "__data" && File.Exists(infoPath))
                File.Copy(infoPath, Path.Combine(Path.GetDirectoryName(destination)!, "__info"));
            return file.file.Metadata.TargetPlatform.ToString();
        }
        finally { manager.UnloadAll(); }
    }
}
