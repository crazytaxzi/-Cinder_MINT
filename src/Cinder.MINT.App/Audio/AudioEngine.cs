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
    private readonly Dictionary<Guid, InputCaptureHub> _inputHubs = [];
    private readonly List<WasapiOut> _outputs = [];
    private bool _disposed;

    public AudioEngine(AudioDeviceService devices)
    {
        _devices = devices;
    }

    public AudioLevelState Levels { get; } = new();
    public bool IsRunning { get; private set; }
    public event EventHandler<string>? Faulted;

    public void Start(AudioGraphModel graph)
    {
        Stop();

        try
        {
            if (!graph.Validate(out string validationError))
                throw new InvalidOperationException(validationError);

            List<AudioNodeModel> outputNodes = graph.Nodes
                .Where(x => x.Type == AudioNodeType.Output && graph.Incoming(x).Count > 0)
                .ToList();

            foreach (AudioNodeModel outputNode in outputNodes)
            {
                ISampleProvider provider = BuildNode(graph, outputNode, []);
                AudioEndpointChoice endpoint = outputNode.Endpoint
                    ?? throw new InvalidOperationException($"Choose an endpoint for {outputNode.Title}.");

                provider = new RunawayFeedbackGuardSampleProvider(
                    provider,
                    () => Faulted?.Invoke(
                        this,
                        $"FEEDBACK GUARD — muted {outputNode.Title} after detecting a sustained full-scale loop."));

                MMDevice outputDevice = _devices.Resolve(endpoint.Id);
                var output = new WasapiOut(
                    outputDevice,
                    AudioClientShareMode.Shared,
                    true,
                    outputNode.LatencyMs);

                output.PlaybackStopped += OnPlaybackStopped;
                output.Init(provider);
                _outputs.Add(output);
            }

            foreach (InputCaptureHub hub in _inputHubs.Values)
                hub.Start();

            foreach (WasapiOut output in _outputs)
                output.Play();

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

        // Silence outputs before tearing down capture domains. This prevents a
        // partially disposed graph from being heard during shutdown.
        foreach (WasapiOut output in _outputs)
        {
            try { output.Stop(); } catch { }
        }

        foreach (WasapiOut output in _outputs)
            output.Dispose();
        _outputs.Clear();

        foreach (InputCaptureHub hub in _inputHubs.Values)
            hub.Dispose();
        _inputHubs.Clear();

        Levels.VoiceActivity = 0;
        Levels.VoicePeakDb = -90;
        Levels.ProgramPeakDb = -90;
        Levels.MasterPeakDb = -90;
    }

    private ISampleProvider BuildNode(
        AudioGraphModel graph,
        AudioNodeModel node,
        HashSet<Guid> buildStack)
    {
        if (!buildStack.Add(node.Id))
            throw new InvalidOperationException($"Audio cycle detected at {node.Title}.");

        try
        {
            return node.Type switch
            {
                AudioNodeType.Input => BuildInput(node),
                AudioNodeType.Mixer => BuildMixer(graph, node, buildStack),
                AudioNodeType.Ducker => BuildDucker(graph, node, buildStack),
                AudioNodeType.Output => BuildSingleInput(graph, node, "IN", buildStack),
                _ => BuildProcessor(graph, node, buildStack)
            };
        }
        finally
        {
            buildStack.Remove(node.Id);
        }
    }

    private ISampleProvider BuildInput(AudioNodeModel node)
    {
        AudioEndpointChoice endpoint = node.Endpoint
            ?? throw new InvalidOperationException($"Choose an endpoint for {node.Title}.");

        if (!_inputHubs.TryGetValue(node.Id, out InputCaptureHub? hub))
        {
            IWaveIn capture = CreateCapture(endpoint, node.LatencyMs);
            hub = new InputCaptureHub(
                capture,
                node.Title,
                message => Faulted?.Invoke(this, message));
            _inputHubs.Add(node.Id, hub);
        }

        // Every branch receives its own buffer. The capture session is shared,
        // but samples are never consumed from another branch's queue.
        ISampleProvider isolatedBranch = hub.CreateSubscriber();
        DspConfiguration config = EmptyConfiguration(
            node.Profile,
            node.IsVoiceActivitySource,
            !node.IsVoiceActivitySource,
            false);

        return new MintDspSampleProvider(isolatedBranch, config, Levels);
    }

    private ISampleProvider BuildMixer(
        AudioGraphModel graph,
        AudioNodeModel node,
        HashSet<Guid> buildStack)
    {
        List<AudioConnectionModel> incoming = graph.Incoming(node, "MIX IN").ToList();
        if (incoming.Count == 0)
            throw new InvalidOperationException($"{node.Title} has no connected inputs.");

        List<ISampleProvider> providers = incoming
            .Select(connection => BuildConnectionSource(graph, connection, buildStack))
            .ToList();

        // This is the only audible summing point in the engine.
        ISampleProvider mixed = providers.Count == 1
            ? providers[0]
            : new MixingSampleProvider(providers) { ReadFully = true };

        if (!node.Enabled) return mixed;
        return new MintDspSampleProvider(
            mixed,
            EmptyConfiguration(node.Profile, false, false, false),
            Levels);
    }

    private ISampleProvider BuildDucker(
        AudioGraphModel graph,
        AudioNodeModel node,
        HashSet<Guid> buildStack)
    {
        ISampleProvider main = BuildSingleInput(graph, node, "MAIN", buildStack);
        if (!node.Enabled) return main;

        AudioConnectionModel? sidechainConnection = graph.Incoming(node, "SIDECHAIN").FirstOrDefault();
        if (sidechainConnection is null)
            return main;

        // The sidechain is analyzed only. It is never added to the audible MAIN path.
        ISampleProvider sidechain = BuildConnectionSource(graph, sidechainConnection, buildStack);
        return new SidechainDuckerSampleProvider(main, sidechain, node.Profile);
    }

    private ISampleProvider BuildProcessor(
        AudioGraphModel graph,
        AudioNodeModel node,
        HashSet<Guid> buildStack)
    {
        ISampleProvider input = BuildSingleInput(graph, node, "IN", buildStack);
        if (!node.Enabled) return input;

        DspConfiguration config = EmptyConfiguration(node.Profile, false, false, false);

        switch (node.Type)
        {
            case AudioNodeType.Gain:
                break;
            case AudioNodeType.NoiseGate:
                config.IsVoice = true;
                config.GateEnabled = true;
                break;
            case AudioNodeType.HighPass:
                config.IsVoice = true;
                config.HighPassEnabled = true;
                break;
            case AudioNodeType.DeEsser:
                config.IsVoice = true;
                config.DeEsserEnabled = true;
                break;
            case AudioNodeType.Equalizer:
                config.EqEnabled = true;
                break;
            case AudioNodeType.LevelRider:
                config.RiderEnabled = true;
                break;
            case AudioNodeType.Compressor:
                config.CompressorEnabled = true;
                break;
            case AudioNodeType.Limiter:
                config.IsMaster = true;
                config.LimiterEnabled = true;
                break;
            default:
                throw new InvalidOperationException($"Unsupported processor node: {node.Type}.");
        }

        return new MintDspSampleProvider(input, config, Levels);
    }

    private ISampleProvider BuildSingleInput(
        AudioGraphModel graph,
        AudioNodeModel node,
        string portName,
        HashSet<Guid> buildStack)
    {
        List<AudioConnectionModel> incoming = graph.Incoming(node, portName).ToList();
        if (incoming.Count == 0)
            throw new InvalidOperationException($"Connect something to {node.Title} · {portName}.");
        if (incoming.Count > 1)
            throw new InvalidOperationException($"{node.Title} · {portName} accepts only one cable. Use a MIX BUS to combine signals.");

        return BuildConnectionSource(graph, incoming[0], buildStack);
    }

    private ISampleProvider BuildConnectionSource(
        AudioGraphModel graph,
        AudioConnectionModel connection,
        HashSet<Guid> buildStack)
    {
        AudioNodeModel source = graph.SourceNode(connection)
            ?? throw new InvalidOperationException("A cable points to a missing source node.");
        return BuildNode(graph, source, buildStack);
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

    private static DspConfiguration EmptyConfiguration(
        MintProfile profile,
        bool isVoice,
        bool isProgram,
        bool isMaster) =>
        new()
        {
            Profile = profile,
            IsVoice = isVoice,
            IsProgram = isProgram,
            IsMaster = isMaster,
            GateEnabled = false,
            HighPassEnabled = false,
            DeEsserEnabled = false,
            EqEnabled = false,
            RiderEnabled = false,
            CompressorEnabled = false,
            DuckerEnabled = false,
            LimiterEnabled = false
        };

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

    private sealed class InputCaptureHub : IDisposable
    {
        private readonly IWaveIn _capture;
        private readonly string _title;
        private readonly Action<string> _faulted;
        private readonly List<BufferedWaveProvider> _subscribers = [];
        private readonly object _sync = new();
        private bool _running;
        private bool _disposed;

        public InputCaptureHub(IWaveIn capture, string title, Action<string> faulted)
        {
            _capture = capture;
            _title = title;
            _faulted = faulted;
            _capture.DataAvailable += CaptureOnDataAvailable;
            _capture.RecordingStopped += CaptureOnRecordingStopped;
        }

        public ISampleProvider CreateSubscriber()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(InputCaptureHub));

            BufferedWaveProvider buffer = CreateBuffer(_capture.WaveFormat);
            lock (_sync)
                _subscribers.Add(buffer);

            return Normalize(buffer);
        }

        public void Start()
        {
            if (_running || _disposed) return;
            _running = true;
            _capture.StartRecording();
        }

        private void CaptureOnDataAvailable(object? sender, WaveInEventArgs e)
        {
            BufferedWaveProvider[] subscribers;
            lock (_sync)
                subscribers = _subscribers.ToArray();

            foreach (BufferedWaveProvider subscriber in subscribers)
            {
                try
                {
                    subscriber.AddSamples(e.Buffer, 0, e.BytesRecorded);
                }
                catch (Exception ex)
                {
                    _faulted($"{_title} isolated branch buffer failed: {ex.Message}");
                }
            }
        }

        private void CaptureOnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (_running && e.Exception is not null)
                _faulted($"{_title} capture stopped: {e.Exception.Message}");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _running = false;

            try { _capture.StopRecording(); } catch { }
            _capture.DataAvailable -= CaptureOnDataAvailable;
            _capture.RecordingStopped -= CaptureOnRecordingStopped;
            _capture.Dispose();

            lock (_sync)
                _subscribers.Clear();
        }
    }
}
