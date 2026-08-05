using Cinder.MINT.Models;
using NAudio.CoreAudioApi;

namespace Cinder.MINT.Audio;

public sealed class AudioDeviceService
{
    public IReadOnlyList<AudioEndpointChoice> GetVoiceSources()
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<AudioEndpointChoice>();

        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            result.Add(new AudioEndpointChoice(
                device.ID,
                device.FriendlyName,
                EndpointSourceKind.Capture,
                DataFlow.Capture));
        }

        foreach (MMDevice device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            result.Add(new AudioEndpointChoice(
                device.ID,
                device.FriendlyName,
                EndpointSourceKind.RenderLoopback,
                DataFlow.Render));
        }

        return result.OrderBy(x => x.Kind).ThenBy(x => x.Name).ToList();
    }

    public IReadOnlyList<AudioEndpointChoice> GetProgramSources()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator
            .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .Select(device => new AudioEndpointChoice(
                device.ID,
                device.FriendlyName,
                EndpointSourceKind.RenderLoopback,
                DataFlow.Render))
            .OrderBy(x => x.Name)
            .ToList();
    }

    public IReadOnlyList<AudioEndpointChoice> GetOutputs() => GetProgramSources();

    public MMDevice Resolve(string id)
    {
        var enumerator = new MMDeviceEnumerator();
        return enumerator.GetDevice(id);
    }
}
