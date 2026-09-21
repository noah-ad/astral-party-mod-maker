using Mono.Cecil;
using Mono.Cecil.Cil;

namespace JixModMaker;

// Fits only the video mesh. The graph's layout, pivot, relations and hit area stay intact.
internal static class PortraitAspectPatch
{
    public static void Apply(ModuleDefinition module, TypeDefinition manager, TypeDefinition[] resources)
    {
        var ts = module.TypeSystem;
        TypeDefinition Required(string name) => module.GetType(name) ?? throw new InvalidDataException("缺少显示类型：" + name);
        var graph = Required("FairyGUI.GGraph");
        var graphics = Required("FairyGUI.NGraphics");
        var vertexBuffer = Required("FairyGUI.VertexBuffer");
        var factory = Required("FairyGUI.IMeshFactory");
        var rect = vertexBuffer.Fields.Single(f => f.Name == "contentRect").FieldType;
        var color = vertexBuffer.Fields.Single(f => f.Name == "vertexColor").FieldType;
        MethodReference Method(TypeReference owner, string name, TypeReference result, bool instance, params TypeReference[] args)
        {
            var method = new MethodReference(name, result, owner) { HasThis = instance };
            foreach (var arg in args) method.Parameters.Add(new ParameterDefinition(arg));
            return method;
        }
        var mesh = module.GetType("Jix.DynamicRendering.PortraitMesh");
        if (mesh == null)
        {
            mesh = new TypeDefinition("Jix.DynamicRendering", "PortraitMesh", TypeAttributes.NotPublic | TypeAttributes.Sealed, ts.Object);
            module.Types.Add(mesh);
            mesh.Interfaces.Add(new InterfaceImplementation(factory));
            mesh.Fields.Add(new FieldDefinition("Original", FieldAttributes.Assembly, factory));
            mesh.Fields.Add(new FieldDefinition("Aspect", FieldAttributes.Assembly, ts.Single));
            var ctor = new MethodDefinition(".ctor", MethodAttributes.Assembly | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName, ts.Void);
            mesh.Methods.Add(ctor);
            var cil = ctor.Body.GetILProcessor();
            cil.Emit(OpCodes.Ldarg_0);
            cil.Emit(OpCodes.Call, Method(ts.Object, ".ctor", ts.Void, true));
            cil.Emit(OpCodes.Ret);
            var populate = new MethodDefinition("OnPopulateMesh", MethodAttributes.Public | MethodAttributes.Final | MethodAttributes.Virtual | MethodAttributes.NewSlot | MethodAttributes.HideBySig, ts.Void);
            populate.Parameters.Add(new ParameterDefinition(vertexBuffer));
            mesh.Methods.Add(populate);
        }
        var original = mesh.Fields.Single(f => f.Name == "Original");
        var aspect = mesh.Fields.Single(f => f.Name == "Aspect");
        var build = mesh.Methods.Single(m => m.Name == "OnPopulateMesh");
        build.Body = new MethodBody(build) { InitLocals = true };
        var bounds = new VariableDefinition(rect);
        var width = new VariableDefinition(ts.Single);
        var height = new VariableDefinition(ts.Single);
        build.Body.Variables.Add(bounds);
        build.Body.Variables.Add(width);
        build.Body.Variables.Add(height);
        var il = build.Body.GetILProcessor();
        var end = Instruction.Create(OpCodes.Ret);
        void BoundsValue(string name)
        {
            il.Emit(OpCodes.Ldloca, bounds);
            il.Emit(OpCodes.Call, Method(rect, "get_" + name, ts.Single, true));
        }
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, vertexBuffer.Fields.Single(f => f.Name == "contentRect"));
        il.Emit(OpCodes.Stloc, bounds);
        BoundsValue("width");
        il.Emit(OpCodes.Ldc_R4, 0f);
        il.Emit(OpCodes.Ble_Un, end);
        BoundsValue("height");
        il.Emit(OpCodes.Ldc_R4, 0f);
        il.Emit(OpCodes.Ble_Un, end);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, aspect);
        il.Emit(OpCodes.Ldc_R4, 0f);
        il.Emit(OpCodes.Ble_Un, end);
        BoundsValue("width");
        BoundsValue("height");
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, aspect);
        il.Emit(OpCodes.Mul);
        var math = new TypeReference("System", "Math", module, ts.CoreLibrary);
        il.Emit(OpCodes.Call, Method(math, "Min", ts.Single, false, ts.Single, ts.Single));
        il.Emit(OpCodes.Stloc, width);
        il.Emit(OpCodes.Ldloc, width);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, aspect);
        il.Emit(OpCodes.Div);
        il.Emit(OpCodes.Stloc, height);
        il.Emit(OpCodes.Ldarg_1);
        BoundsValue("x");
        BoundsValue("width");
        il.Emit(OpCodes.Ldloc, width);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_R4, .5f);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Add);
        BoundsValue("y");
        BoundsValue("height");
        il.Emit(OpCodes.Ldloc, height);
        il.Emit(OpCodes.Sub);
        il.Emit(OpCodes.Ldc_R4, .5f);
        il.Emit(OpCodes.Mul);
        il.Emit(OpCodes.Add);
        il.Emit(OpCodes.Ldloc, width);
        il.Emit(OpCodes.Ldloc, height);
        il.Emit(OpCodes.Newobj, Method(rect, ".ctor", ts.Void, true, ts.Single, ts.Single, ts.Single, ts.Single));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, vertexBuffer.Fields.Single(f => f.Name == "vertexColor"));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldfld, vertexBuffer.Fields.Single(f => f.Name == "uvRect"));
        il.Emit(OpCodes.Callvirt, Method(vertexBuffer, "AddQuad", ts.Void, true, rect, color, rect));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_I4_0);
        il.Emit(OpCodes.Callvirt, Method(vertexBuffer, "AddTriangles", ts.Void, true, ts.Int32));
        il.Append(end);

        var install = manager.Methods.SingleOrDefault(m => m.Name == "JixFitPortraitMesh");
        if (install == null)
        {
            install = new MethodDefinition("JixFitPortraitMesh", MethodAttributes.Private | MethodAttributes.Static, ts.Void);
            install.Parameters.Add(new ParameterDefinition(ts.String));
            install.Parameters.Add(new ParameterDefinition(graph));
            manager.Methods.Add(install);
            foreach (string name in new[] { "Play", "PlaAutoReleaseVideo" })
            {
                var play = manager.Methods.Single(m => m.Name == name && m.Parameters.Count >= 2 && m.Parameters[1].ParameterType.FullName == graph.FullName);
                var entry = play.Body.Instructions[0];
                foreach (var instruction in new[] { Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldarg_2), Instruction.Create(OpCodes.Call, install) })
                    play.Body.GetILProcessor().InsertBefore(entry, instruction);
            }
            var stop = manager.Methods.Single(m => m.Name == "StopAndDestroy" && m.Parameters.Count == 1);
            var stopEntry = stop.Body.Instructions[0];
            foreach (var instruction in new[] { Instruction.Create(OpCodes.Ldnull), Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Call, install) })
                stop.Body.GetILProcessor().InsertBefore(stopEntry, instruction);
        }
        install.Body = new MethodBody(install) { InitLocals = true };
        var target = new VariableDefinition(graphics);
        var adapter = new VariableDefinition(mesh);
        var ratio = new VariableDefinition(ts.Single);
        install.Body.Variables.Add(target);
        install.Body.Variables.Add(adapter);
        install.Body.Variables.Add(ratio);
        il = install.Body.GetILProcessor();
        end = Instruction.Create(OpCodes.Ret);
        var configure = Instruction.Create(OpCodes.Nop);
        var restore = Instruction.Create(OpCodes.Nop);
        var assign = Instruction.Create(OpCodes.Nop);
        var shape = graph.Methods.Single(m => m.Name == "get_shape");
        var display = Required("FairyGUI.DisplayObject");
        var getGraphics = display.Methods.Single(m => m.Name == "get_graphics");
        var getFactory = graphics.Methods.Single(m => m.Name == "get_meshFactory");
        var setFactory = graphics.Methods.Single(m => m.Name == "set_meshFactory");
        var dirty = graphics.Methods.Single(m => m.Name == "SetMeshDirty" && m.Parameters.Count == 0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse, end);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, shape);
        il.Emit(OpCodes.Callvirt, getGraphics);
        il.Emit(OpCodes.Stloc, target);
        il.Emit(OpCodes.Ldloc, target);
        il.Emit(OpCodes.Callvirt, getFactory);
        il.Emit(OpCodes.Isinst, mesh);
        il.Emit(OpCodes.Stloc, adapter);
        var equality = Method(ts.String, "op_Equality", ts.Boolean, false, ts.String, ts.String);
        foreach (var resource in resources)
        {
            float value = ResourceAspect(resource);
            var next = Instruction.Create(OpCodes.Nop);
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, (string)resource.Fields.Single(f => f.Name == "Key").Constant);
            il.Emit(OpCodes.Call, equality);
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldc_R4, value);
            il.Emit(OpCodes.Stloc, ratio);
            il.Emit(OpCodes.Br, configure);
            il.Append(next);
        }
        il.Emit(OpCodes.Br, restore);
        il.Append(configure);
        il.Emit(OpCodes.Ldloc, adapter);
        il.Emit(OpCodes.Brtrue, assign);
        il.Emit(OpCodes.Newobj, mesh.Methods.Single(m => m.Name == ".ctor"));
        il.Emit(OpCodes.Stloc, adapter);
        il.Emit(OpCodes.Ldloc, adapter);
        il.Emit(OpCodes.Ldloc, target);
        il.Emit(OpCodes.Callvirt, getFactory);
        il.Emit(OpCodes.Stfld, original);
        il.Append(assign);
        il.Emit(OpCodes.Ldloc, adapter);
        il.Emit(OpCodes.Ldloc, ratio);
        il.Emit(OpCodes.Stfld, aspect);
        il.Emit(OpCodes.Ldloc, target);
        il.Emit(OpCodes.Ldloc, adapter);
        il.Emit(OpCodes.Callvirt, setFactory);
        il.Emit(OpCodes.Ldloc, target);
        il.Emit(OpCodes.Callvirt, dirty);
        il.Emit(OpCodes.Ret);
        il.Append(restore);
        il.Emit(OpCodes.Ldloc, adapter);
        il.Emit(OpCodes.Brfalse, end);
        il.Emit(OpCodes.Ldloc, target);
        il.Emit(OpCodes.Ldloc, adapter);
        il.Emit(OpCodes.Ldfld, original);
        il.Emit(OpCodes.Callvirt, setFactory);
        il.Append(end);
    }

    private static float ResourceAspect(TypeDefinition resource)
    {
        var field = resource.Fields.SingleOrDefault(f => f.Name == "AspectRatio");
        if (field != null) return (float)field.Constant;
        // preview.7 assets predate the explicit ratio field; read their verified factory metadata.
        int Dimension(string name)
        {
            var instructions = resource.Methods.Single(m => m.Name == "Create").Body.Instructions;
            var marker = instructions.Single(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == name);
            var constants = instructions.Skip(instructions.IndexOf(marker) + 1).Take(6).Where(i => i.OpCode == OpCodes.Ldc_I4).ToArray();
            if (constants.Length != 2 || (int)constants[0].Operand != 20 || (int)constants[1].Operand <= 0)
                throw new InvalidDataException("旧独立视频尺寸不可识别，请重新导入该视频。");
            return (int)constants[1].Operand;
        }
        return Dimension("width") / (float)Dimension("height");
    }
}
