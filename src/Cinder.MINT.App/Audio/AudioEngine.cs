using Cinder.MINT.Audio.Dsp;
using Cinder.MINT.Models;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Cinder.MINT.Audio;

public sealed class AudioEngine : IDisposable
{
    private const int EngineSampleRate = 48000;
    private const int EngineChannels = 2;

    private readonly AudioDeviceService _devices;
    private IWaveIn? _voiceCapture;
    private IWaveIn? _programCapture;
    private WasapiOut? _output;
    private BufferedWaveProvider? _voiceBuffer;
    private BufferedWaveProvider? _programBuffer;
    private bool _disposed;

    public AudioEngine(AudioDeviceService devices)
    {
        _devices = devices;
    }

    public AudioLevelState Levels { get; } = new();

    public bool IsRunning { get; private set; }

    public event EventHandler<string>? Faulted;

    public void Start(
        AudioEndpointChoice voiceSource,
        AudioEndpointChoice programSource,
        AudioEndpointChoice output,
        DspConfiguration voiceConfig,
        DspConfiguration programConfig,
        DspConfiguration masterConfig,
        int latencyMs = 30)
    {
        Stop();

        try
        {
            if (programSource.Id == output.Id)
                throw new InvalidOperationException(
                    "The music/app loopback endpoint cannot also be the MINT output. " +
                    "That creates a feedback loop. Choose VB-Cable or another dedicated output.");

            if (voiceSource.Kind == EndpointSourceKind.RenderLoopback && voiceSource.Id == output.Id)
                throw new InvalidOperationException(
                    "The RVC/voice loopback endpoint cannot also be the MINT output. " +
                    "Use separate virtual endpoints for RVC input and final stream output.");

            _voiceCapture = CreateCapture(voiceSource, latencyMs);
            _programCapture = CreateCapture(programSource, latencyMs);

            _voiceBuffer = CreateBuffer(_voiceCapture.WaveFormat);
            _programBuffer = CreateBuffer(_programCapture.WaveFormat);

            AttachCapture(_voiceCapture, _voiceBuffer, "Voice/RVC");
            AttachCapture(_programCapture, _programBuffer, "Music/App");

            ISampleProvider voice = Normalize(_voiceBuffer);
            ISampleProvider program = Normalize(_programBuffer);

            var voiceDsp = new MintDspSampleProvider(voice, voiceConfig, Levels);
            var programDsp = new MintDspSampleProvider(program, programConfig, Levels);

            var mixer = new MixingSampleProvider(new[] { voiceDsp, programDsp })
            {
                ReadFully = true
            };

            var master = new MintDspSampleProvider(mixer, masterConfig, Levels);

            using MMDevice outputDevice = _devices.Resolve(output.Id);
            _output = new WasapiOut(outputDevice, AudioClientShareMode.Shared, true, latencyMs);
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Init(master);

            _voiceCapture.StartRecording();
            _programCapture.StartRecording();
            _output.Play();

            IsRunning = true;
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        IsRunning = false;

        try { _voiceCapture?.StopRecording(); } catch { }
        try { _programCapture?.StopRecording(); } catch { }
        try { _output?.Stop(); } catch { }

        _voiceCapture?.Dispose();
        _programCapture?.Dispose();
        _output?.Dispose();

        _voiceCapture = null;
        _programCapture = null;
        _output = null;
        _voiceBuffer = null;
        _programBuffer = null;

        Levels.VoiceActivity = 0;
        Levels.VoicePeakDb = -90;
        Levels.ProgramPeakDb = -90;
        Levels.MasterPeakDb = -90;
    }

    private IWaveIn CreateCapture(AudioEndpointChoice source, int latencyMs)
    {
        MMDevice device = _devices.Resolve(source.Id);

        if (source.Kind == EndpointSourceKind.RenderLoopback)
            return new WasapiLoopbackCapture(device);

        return new WasapiCapture(device, true, latencyMs);
    }

    private static BufferedWaveProvider CreateBuffer(WaveFormat format) =>
        new(format)
        {
            BufferDuration = TimeSpan.FromSeconds(3),
            DiscardOnBufferOverflow = true,
            ReadFully = true
        };

    private void AttachCapture(IWaveIn capture, BufferedWaveProvider buffer, string lane)
    {
        capture.DataAvailable += (_, e) =>
        {
            try
            {
                buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
            }
            catch (Exception ex)
            {
                Faulted?.Invoke(this, $"{lane} buffer failed: {ex.Message}");
            }
        };

        capture.RecordingStopped += (_, e) =>
        {
            if (IsRunning && e.Exception is not null)
                Faulted?.Invoke(this, $"{lane} capture stopped: {e.Exception.Message}");
        };
    }

    private static ISampleProvider Normalize(BufferedWaveProvider buffer)
    {
        ISampleProvider provider = buffer.ToSampleProvider();

        provider = provider.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(provider),
            2 => provider,
            _ => new StereoDownmixSampleProvider(provider)
        };

        if (provider.WaveFormat.SampleRate != EngineSampleRate)
            provider = new WdlResamplingSampleProvider(provider, EngineSampleRate);

        if (provider.WaveFormat.Channels != EngineChannels)
            throw new InvalidOperationException("Unable to normalize source to stereo.");

        return provider;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (IsRunning && e.Exception is not null)
            Faulted?.Invoke(this, $"Output stopped: {e.Exception.Message}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
