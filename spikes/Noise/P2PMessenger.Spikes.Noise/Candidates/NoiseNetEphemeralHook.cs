using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using NSec.Cryptography;
using NoiseNet = Noise;

namespace P2PMessenger.Spikes.Noise.Candidates;

internal static class NoiseNetEphemeralHook
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string DhInterfaceName = "Noise.Dh";

    private static readonly Lazy<HookState> State = new(Build);

    public static bool IsAvailable(out string reason)
    {
        HookState state = State.Value;
        reason = state.Reason;
        return state.WrapperType is not null;
    }

    public static void Apply(NoiseNet.HandshakeState handshake, ReadOnlySpan<byte> ephemeralPrivateKey)
    {
        HookState state = State.Value;
        Type wrapperType = state.WrapperType ?? throw new NotSupportedException(state.Reason);
        Inject(wrapperType, handshake, ephemeralPrivateKey);
    }

    private static void Inject(Type wrapperType, NoiseNet.HandshakeState handshake, ReadOnlySpan<byte> ephemeralPrivateKey)
    {
        Type handshakeType = handshake.GetType();
        FieldInfo dhField = handshakeType.GetField("dh", InstanceMembers)
            ?? throw new MissingFieldException(handshakeType.Name, "dh");
        MethodInfo setDh = handshakeType.GetMethod("SetDh", InstanceMembers)
            ?? throw new MissingMethodException(handshakeType.Name, "SetDh");
        object inner = dhField.GetValue(handshake)
            ?? throw new InvalidOperationException("Noise.NET's handshake state has no DH object.");
        object wrapper = Activator.CreateInstance(wrapperType, inner, ephemeralPrivateKey.ToArray())
            ?? throw new InvalidOperationException("The DH wrapper could not be created.");
        setDh.Invoke(handshake, [wrapper]);
    }

    private static HookState Build()
    {
        if (!RuntimeFeature.IsDynamicCodeSupported)
        {
            return new HookState(null, "this runtime cannot generate code at run time");
        }

        try
        {
            Type wrapperType = DefineWrapperType();
            Probe(wrapperType);
            return new HookState(wrapperType, "set through Noise.NET's internal test hook (HandshakeState.SetDh), reached by reflection");
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            Exception cause = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
            return new HookState(null, $"Noise.NET's internal test hook is not reachable here: {cause.GetType().Name}: {cause.Message}");
        }
    }

    private static Type DefineWrapperType()
    {
        Assembly noiseAssembly = typeof(NoiseNet.Protocol).Assembly;
        Type dhInterface = noiseAssembly.GetType(DhInterfaceName, throwOnError: true)
            ?? throw new TypeLoadException(DhInterfaceName);

        AssemblyName name = new("P2PMessenger.Spikes.Noise.EphemeralHook");
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule(name.Name ?? "EphemeralHook");
        ConstructorInfo ignoresAccessChecks = DefineIgnoresAccessChecksToAttribute(module);
        assembly.SetCustomAttribute(new CustomAttributeBuilder(ignoresAccessChecks, [noiseAssembly.GetName().Name ?? "Noise"]));

        TypeBuilder type = module.DefineType(
            "FixedEphemeralDh",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            typeof(object),
            [dhInterface]);
        FieldBuilder innerField = type.DefineField("_inner", dhInterface, FieldAttributes.Private | FieldAttributes.InitOnly);
        FieldBuilder keyField = type.DefineField("_ephemeral", typeof(byte[]), FieldAttributes.Private | FieldAttributes.InitOnly);

        ConstructorBuilder constructor = type.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [dhInterface, typeof(byte[])]);
        ILGenerator constructorBody = constructor.GetILGenerator();
        constructorBody.Emit(OpCodes.Ldarg_0);
        constructorBody.Emit(OpCodes.Call, typeof(object).GetConstructor(Type.EmptyTypes) ?? throw new MissingMethodException("object", ".ctor"));
        constructorBody.Emit(OpCodes.Ldarg_0);
        constructorBody.Emit(OpCodes.Ldarg_1);
        constructorBody.Emit(OpCodes.Stfld, innerField);
        constructorBody.Emit(OpCodes.Ldarg_0);
        constructorBody.Emit(OpCodes.Ldarg_2);
        constructorBody.Emit(OpCodes.Stfld, keyField);
        constructorBody.Emit(OpCodes.Ret);

        MethodInfo generateFromKey = dhInterface.GetMethod("GenerateKeyPair", [typeof(ReadOnlySpan<byte>)])
            ?? throw new MissingMethodException(DhInterfaceName, "GenerateKeyPair(ReadOnlySpan<byte>)");
        MethodInfo toSpan = typeof(ReadOnlySpan<byte>).GetMethod("op_Implicit", [typeof(byte[])])
            ?? throw new MissingMethodException("ReadOnlySpan<byte>", "op_Implicit(byte[])");

        foreach (MethodInfo method in dhInterface.GetMethods())
        {
            Type[] parameterTypes = [.. method.GetParameters().Select(parameter => parameter.ParameterType)];
            MethodBuilder implementation = type.DefineMethod(
                method.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                method.ReturnType,
                parameterTypes);
            ILGenerator body = implementation.GetILGenerator();
            body.Emit(OpCodes.Ldarg_0);
            body.Emit(OpCodes.Ldfld, innerField);
            if (method.Name == "GenerateKeyPair" && parameterTypes.Length == 0)
            {
                body.Emit(OpCodes.Ldarg_0);
                body.Emit(OpCodes.Ldfld, keyField);
                body.Emit(OpCodes.Call, toSpan);
                body.Emit(OpCodes.Callvirt, generateFromKey);
            }
            else
            {
                for (short index = 1; index <= parameterTypes.Length; index++)
                {
                    body.Emit(OpCodes.Ldarg, index);
                }

                body.Emit(OpCodes.Callvirt, method);
            }

            body.Emit(OpCodes.Ret);
            type.DefineMethodOverride(implementation, method);
        }

        return type.CreateType();
    }

    private static ConstructorInfo DefineIgnoresAccessChecksToAttribute(ModuleBuilder module)
    {
        TypeBuilder attribute = module.DefineType(
            "System.Runtime.CompilerServices.IgnoresAccessChecksToAttribute",
            TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class,
            typeof(Attribute));
        ConstructorBuilder constructor = attribute.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, [typeof(string)]);
        ILGenerator body = constructor.GetILGenerator();
        body.Emit(OpCodes.Ldarg_0);
        body.Emit(OpCodes.Call, typeof(Attribute).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes)
            ?? throw new MissingMethodException("Attribute", ".ctor"));
        body.Emit(OpCodes.Ret);
        return attribute.CreateType().GetConstructor([typeof(string)])
            ?? throw new MissingMethodException("IgnoresAccessChecksToAttribute", ".ctor(string)");
    }

    private static void Probe(Type wrapperType)
    {
        using Key initiatorStatic = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        using Key responderStatic = Key.Create(KeyAgreementAlgorithm.X25519);
        using Key ephemeral = Key.Create(KeyAgreementAlgorithm.X25519, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        byte[] ephemeralPrivateKey = ephemeral.Export(KeyBlobFormat.RawPrivateKey);
        byte[] staticPrivateKey = initiatorStatic.Export(KeyBlobFormat.RawPrivateKey);

        NoiseNet.Protocol protocol = NoiseNet.Protocol.Parse("Noise_KK_25519_ChaChaPoly_BLAKE2b".AsSpan());
        using NoiseNet.HandshakeState handshake = protocol.Create(
            true,
            default,
            staticPrivateKey,
            responderStatic.PublicKey.Export(KeyBlobFormat.RawPublicKey));
        Inject(wrapperType, handshake, ephemeralPrivateKey);

        byte[] message = new byte[NoiseNet.Protocol.MaxMessageLength];
        _ = handshake.WriteMessage(default, message);
        byte[] expected = ephemeral.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        if (!message.AsSpan(0, expected.Length).SequenceEqual(expected))
        {
            throw new InvalidOperationException("Noise.NET did not use the given ephemeral key.");
        }
    }

    private sealed record HookState(Type? WrapperType, string Reason);
}
