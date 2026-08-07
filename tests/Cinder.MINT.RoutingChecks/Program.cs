using Cinder.MINT.Audio.AI;
using Cinder.MINT.Models;
using NAudio.CoreAudioApi;

var checks = new (string Name, Action Run)[]
{
    ("safe explicit mix validates", SafeExplicitMixValidates),
    ("ordinary processor rejects audible fan-in", ProcessorRejectsFanIn),
    ("graph cycle is rejected while patching", GraphCycleIsRejected),
    ("duplicate input endpoint is rejected", DuplicateInputIsRejected),
    ("duplicate output writer is rejected", DuplicateOutputIsRejected),
    ("virtual cable return path is rejected", VirtualCableReturnIsRejected),
    ("cross-output endpoint cycle is rejected", CrossOutputCycleIsRejected),
    ("all AI specialists infer finite bounded controls", AiSpecialistsAreBounded),
    ("AI brain sessions keep independent state", AiSessionsAreIndependent),
    ("default AI master lives after explicit mix bus", AiMasterFollowsExplicitMix)
};

foreach ((string name, Action run) in checks)
{
    try
    {
        run();
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL  {name}: {ex.Message}");
        Environment.ExitCode = 1;
    }
}

static void SafeExplicitMixValidates()
{
    var graph = new AudioGraphModel();
    AudioNodeModel mic = Input(graph, "mic", "Microphone (USB Audio)", EndpointSourceKind.Capture);
    AudioNodeModel music = Input(graph, "music", "Speakers (Music Device)", EndpointSourceKind.RenderLoopback);
    AudioNodeModel mixer = graph.AddNode(AudioNodeType.Mixer, 300, 100);
    AudioNodeModel output = Output(graph, "stream", "Speakers (Stream Output)");

    Connect(graph, mic, "OUT", mixer, "MIX IN");
    Connect(graph, music, "OUT", mixer, "MIX IN");
    Connect(graph, mixer, "OUT", output, "IN");

    Require(graph.Validate(out string error), error);
}

static void ProcessorRejectsFanIn()
{
    var graph = new AudioGraphModel();
    AudioNodeModel a = graph.AddNode(AudioNodeType.Input, 0, 0);
    AudioNodeModel b = graph.AddNode(AudioNodeType.Input, 0, 100);
    AudioNodeModel compressor = graph.AddNode(AudioNodeType.Compressor, 200, 50);

    Connect(graph, a, "OUT", compressor, "IN");
    bool secondConnected = graph.TryConnect(b.Id, "OUT", compressor.Id, "IN", out _);
    Require(!secondConnected, "A normal processor accepted a second audible input.");
}

static void GraphCycleIsRejected()
{
    var graph = new AudioGraphModel();
    AudioNodeModel input = graph.AddNode(AudioNodeType.Input, 0, 0);
    AudioNodeModel first = graph.AddNode(AudioNodeType.Gain, 200, 0);
    AudioNodeModel second = graph.AddNode(AudioNodeType.Compressor, 400, 0);

    Connect(graph, input, "OUT", first, "IN");
    Connect(graph, first, "OUT", second, "IN");
    bool cycleConnected = graph.TryConnect(second.Id, "OUT", first.Id, "IN", out _);
    Require(!cycleConnected, "A graph cycle was accepted.");
}

static void DuplicateInputIsRejected()
{
    var graph = new AudioGraphModel();
    AudioNodeModel first = Input(graph, "same-source", "Microphone (Shared USB)", EndpointSourceKind.Capture);
    AudioNodeModel second = Input(graph, "same-source", "Microphone (Shared USB)", EndpointSourceKind.Capture);
    AudioNodeModel mixer = graph.AddNode(AudioNodeType.Mixer, 300, 100);
    AudioNodeModel output = Output(graph, "stream", "Speakers (Stream Output)");

    Connect(graph, first, "OUT", mixer, "MIX IN");
    Connect(graph, second, "OUT", mixer, "MIX IN");
    Connect(graph, mixer, "OUT", output, "IN");

    Require(!graph.Validate(out string error) && error.Contains("more than one active INPUT", StringComparison.OrdinalIgnoreCase), error);
}

static void DuplicateOutputIsRejected()
{
    var graph = new AudioGraphModel();
    AudioNodeModel input = Input(graph, "mic", "Microphone (USB Audio)", EndpointSourceKind.Capture);
    AudioNodeModel first = Output(graph, "same-output", "Speakers (Shared Stream Output)");
    AudioNodeModel second = Output(graph, "same-output", "Speakers (Shared Stream Output)");

    Connect(graph, input, "OUT", first, "IN");
    Connect(graph, input, "OUT", second, "IN");

    Require(!graph.Validate(out string error) && error.Contains("Multiple OUTPUT", StringComparison.OrdinalIgnoreCase), error);
}

static void VirtualCableReturnIsRejected()
{
    var graph = new AudioGraphModel();
    AudioNodeModel input = Input(
        graph,
        "cable-capture",
        "CABLE Output (VB-Audio Virtual Cable)",
        EndpointSourceKind.Capture);
    AudioNodeModel output = Output(
        graph,
        "cable-render",
        "CABLE Input (VB-Audio Virtual Cable)");

    Connect(graph, input, "OUT", output, "IN");

    Require(!graph.Validate(out string error) && error.Contains("feed itself", StringComparison.OrdinalIgnoreCase), error);
}

static void CrossOutputCycleIsRejected()
{
    var graph = new AudioGraphModel();
    AudioNodeModel inputA = Input(graph, "capture-a", "Cable A Output (VB-Audio Cable A)", EndpointSourceKind.Capture);
    AudioNodeModel inputB = Input(graph, "capture-b", "Cable B Output (VB-Audio Cable B)", EndpointSourceKind.Capture);
    AudioNodeModel outputA = Output(graph, "render-a", "Cable A Input (VB-Audio Cable A)");
    AudioNodeModel outputB = Output(graph, "render-b", "Cable B Input (VB-Audio Cable B)");

    Connect(graph, inputA, "OUT", outputB, "IN");
    Connect(graph, inputB, "OUT", outputA, "IN");

    Require(!graph.Validate(out string error) && error.Contains("external audio loop", StringComparison.OrdinalIgnoreCase), error);
}

static void AiSpecialistsAreBounded()
{
    var runtime = new MintAiRuntime();
    var frame = new AiFeatureFrame(
        Loudness: 0.72f,
        Peak: 0.84f,
        Crest: 0.68f,
        LowEnergy: 0.41f,
        MidEnergy: 0.36f,
        HighEnergy: 0.49f,
        Sibilance: 0.78f,
        Transient: 0.73f,
        Noise: 0.52f,
        Harshness: 0.69f,
        Metallicity: 0.76f,
        SpeechProbability: 0.91f);

    foreach (MintAiSpecialist specialist in Enum.GetValues<MintAiSpecialist>())
    {
        var intent = new MintProfile
        {
            AiContentMode = specialist == MintAiSpecialist.Cleanup ? MintAiContentMode.RvcVoice : MintAiContentMode.Auto,
            AiStrength = 0.9f,
            AiNaturalness = 0.8f,
            AiMaxCorrectionDb = 6f,
            AiPreserveTransients = 0.82f,
            AiConsistency = 0.78f,
            AiTargetLoudnessDb = -18f,
            AiAdaptation = 0.6f
        };
        MintProfile controlled = intent.Clone();
        AiBrainSession session = runtime.GetOrCreate(Guid.NewGuid(), specialist);

        for (int i = 0; i < 24; i++)
            session.Evaluate(frame, intent, controlled);

        Require(session.TryGetSnapshot(out AiBrainSnapshot? snapshot) && snapshot is not null,
            $"{specialist} did not publish telemetry.");
        Require(float.IsFinite(snapshot!.Confidence) && snapshot.Confidence is >= 0f and <= 1f,
            $"{specialist} confidence escaped bounds.");
        Require(AllFinite(controlled), $"{specialist} produced a non-finite DSP control.");
        Require(controlled.HighPassHz is >= 30f and <= 220f, $"{specialist} high-pass escaped bounds.");
        Require(controlled.DeEsserAmount is >= 0f and <= 1f, $"{specialist} de-esser escaped bounds.");
        Require(controlled.Compression is >= 0f and <= 1f, $"{specialist} compression escaped bounds.");
        Require(controlled.LowGainDb is >= -12f and <= 12f && controlled.MidGainDb is >= -12f and <= 12f && controlled.HighGainDb is >= -12f and <= 12f,
            $"{specialist} EQ escaped bounds.");
        Require(controlled.LimiterCeilingDb is >= -12f and <= -0.1f,
            $"{specialist} limiter ceiling escaped bounds.");
    }
}

static void AiSessionsAreIndependent()
{
    var runtime = new MintAiRuntime();
    Guid quietId = Guid.NewGuid();
    Guid harshId = Guid.NewGuid();
    AiBrainSession quiet = runtime.GetOrCreate(quietId, MintAiSpecialist.Cleanup);
    AiBrainSession harsh = runtime.GetOrCreate(harshId, MintAiSpecialist.Cleanup);

    var intent = new MintProfile { AiContentMode = MintAiContentMode.RvcVoice, AiStrength = 0.85f };
    MintProfile quietControls = intent.Clone();
    MintProfile harshControls = intent.Clone();

    var quietFrame = new AiFeatureFrame(0.25f, 0.25f, 0.35f, 0.35f, 0.35f, 0.18f, 0.12f, 0.22f, 0.18f, 0.16f, 0.12f, 0.82f);
    var harshFrame = new AiFeatureFrame(0.75f, 0.88f, 0.62f, 0.28f, 0.42f, 0.69f, 0.91f, 0.64f, 0.62f, 0.86f, 0.93f, 0.94f);

    for (int i = 0; i < 30; i++)
    {
        quiet.Evaluate(quietFrame, intent, quietControls);
        harsh.Evaluate(harshFrame, intent, harshControls);
    }

    IReadOnlyDictionary<Guid, AiBrainSnapshot> snapshots = runtime.GetSnapshots();
    Require(snapshots.ContainsKey(quietId) && snapshots.ContainsKey(harshId), "Independent AI node snapshots were lost.");
    Require(!string.Equals(snapshots[quietId].Action, snapshots[harshId].Action, StringComparison.Ordinal),
        "Two AI nodes with different signals collapsed to the same state/action.");
}

static void AiMasterFollowsExplicitMix()
{
    AudioGraphModel graph = AudioGraphModel.CreateDefault();
    AudioNodeModel master = graph.Nodes.Single(x => x.Type == AudioNodeType.AiProcessor && x.AiSpecialist == MintAiSpecialist.Master);
    AudioConnectionModel incoming = graph.Incoming(master).Single();
    AudioNodeModel source = graph.SourceNode(incoming) ?? throw new InvalidOperationException("AI Master input source is missing.");

    Require(source.Type == AudioNodeType.Mixer,
        $"AI Master is fed by {source.Type}, not an explicit Mix Bus.");
}

static bool AllFinite(MintProfile p) =>
    float.IsFinite(p.InputGainDb) &&
    float.IsFinite(p.GateThresholdDb) &&
    float.IsFinite(p.HighPassHz) &&
    float.IsFinite(p.DeEsserAmount) &&
    float.IsFinite(p.LowGainDb) &&
    float.IsFinite(p.MidGainDb) &&
    float.IsFinite(p.HighGainDb) &&
    float.IsFinite(p.TargetDb) &&
    float.IsFinite(p.RiderSpeedMs) &&
    float.IsFinite(p.Compression) &&
    float.IsFinite(p.CompressorAttackMs) &&
    float.IsFinite(p.CompressorReleaseMs) &&
    float.IsFinite(p.LimiterCeilingDb) &&
    float.IsFinite(p.LimiterReleaseMs);

static AudioNodeModel Input(
    AudioGraphModel graph,
    string id,
    string name,
    EndpointSourceKind kind)
{
    AudioNodeModel node = graph.AddNode(AudioNodeType.Input, 0, 0, name);
    node.Endpoint = new AudioEndpointChoice(id, name, kind, kind == EndpointSourceKind.Capture ? DataFlow.Capture : DataFlow.Render);
    return node;
}

static AudioNodeModel Output(AudioGraphModel graph, string id, string name)
{
    AudioNodeModel node = graph.AddNode(AudioNodeType.Output, 600, 0, name);
    node.Endpoint = new AudioEndpointChoice(id, name, EndpointSourceKind.RenderLoopback, DataFlow.Render);
    return node;
}

static void Connect(
    AudioGraphModel graph,
    AudioNodeModel source,
    string sourcePort,
    AudioNodeModel target,
    string targetPort)
{
    Require(graph.TryConnect(source.Id, sourcePort, target.Id, targetPort, out string error), error);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(string.IsNullOrWhiteSpace(message) ? "Check failed." : message);
}
