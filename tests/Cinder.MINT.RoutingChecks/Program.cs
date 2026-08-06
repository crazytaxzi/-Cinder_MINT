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
    ("cross-output endpoint cycle is rejected", CrossOutputCycleIsRejected)
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
