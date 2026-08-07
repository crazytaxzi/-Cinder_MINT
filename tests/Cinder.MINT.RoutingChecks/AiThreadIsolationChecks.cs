using Cinder.MINT.Models;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class AiThreadIsolationChecks
{
    [ModuleInitializer]
    internal static void Run()
    {
        Assembly app = typeof(MintProfile).Assembly;
        Type provider = app.GetType("Cinder.MINT.Audio.AI.AiControlledSampleProvider", throwOnError: true)!;

        FieldInfo[] fields = provider.GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        if (fields.Any(field => field.FieldType == typeof(AudioNodeModel)))
            throw new InvalidOperationException("AI realtime provider retains an AudioNodeModel UI reference.");

        MethodInfo detachedCopy = provider.GetMethod(
            "DetachedCopy",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Detached AI profile copier is missing.");

        var uiProfile = new MintProfile();
        int uiNotifications = 0;
        uiProfile.PropertyChanged += (_, _) => uiNotifications++;

        var runtimeProfile = (MintProfile)(detachedCopy.Invoke(null, [uiProfile])
            ?? throw new InvalidOperationException("Detached profile copier returned null."));

        uiNotifications = 0;
        runtimeProfile.AiStrength = 0.19f;
        runtimeProfile.HighGainDb = -2.4f;

        if (uiNotifications != 0)
            throw new InvalidOperationException("AI runtime profile inherited UI PropertyChanged subscribers.");

        uiProfile.AiStrength = 0.23f;
        if (uiNotifications != 1)
            throw new InvalidOperationException("Original UI profile notification behavior was damaged.");

        Console.WriteLine("PASS  AI realtime profile is detached from UI subscribers/state");
    }
}
