from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def read(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def write(path: str, text: str) -> None:
    (ROOT / path).write_text(text, encoding="utf-8")


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise RuntimeError(f"{label}: expected exactly one match, found {count}")
    return text.replace(old, new, 1)


# -----------------------------------------------------------------------------
# MintProfile: add the noise specialist intent/runtime controls and fix Clone so
# event subscribers can never leak into detached runtime copies.
# -----------------------------------------------------------------------------
path = "src/Cinder.MINT.App/Models/MintProfile.cs"
s = read(path)
s = replace_once(s, "    Cleanup,\n    Tone,", "    Cleanup,\n    Noise,\n    Tone,", "noise enum")
s = replace_once(
    s,
    "    private float _aiAdaptation = 0.55f;\n",
    "    private float _aiAdaptation = 0.55f;\n"
    "    private float _aiNoiseMaxReductionDb = 24f;\n"
    "    private float _aiNoiseReductionDb = 10f;\n"
    "    private float _aiNoiseSensitivity = 0.68f;\n"
    "    private float _aiNoiseSpeechProtection = 0.86f;\n"
    "    private float _aiNoiseLearnRate = 0.035f;\n",
    "noise backing fields",
)
s = replace_once(
    s,
    "    public float AiAdaptation { get => _aiAdaptation; set => SetField(ref _aiAdaptation, Math.Clamp(value, 0f, 1f)); }\n",
    "    public float AiAdaptation { get => _aiAdaptation; set => SetField(ref _aiAdaptation, Math.Clamp(value, 0f, 1f)); }\n"
    "    public float AiNoiseMaxReductionDb { get => _aiNoiseMaxReductionDb; set => SetField(ref _aiNoiseMaxReductionDb, Math.Clamp(value, 6f, 36f)); }\n"
    "    public float AiNoiseReductionDb { get => _aiNoiseReductionDb; set => SetField(ref _aiNoiseReductionDb, Math.Clamp(value, 0f, 36f)); }\n"
    "    public float AiNoiseSensitivity { get => _aiNoiseSensitivity; set => SetField(ref _aiNoiseSensitivity, Math.Clamp(value, 0.05f, 1f)); }\n"
    "    public float AiNoiseSpeechProtection { get => _aiNoiseSpeechProtection; set => SetField(ref _aiNoiseSpeechProtection, Math.Clamp(value, 0f, 1f)); }\n"
    "    public float AiNoiseLearnRate { get => _aiNoiseLearnRate; set => SetField(ref _aiNoiseLearnRate, Math.Clamp(value, 0.001f, 0.25f)); }\n",
    "noise properties",
)
s = replace_once(
    s,
    "    public MintProfile Clone() => (MintProfile)MemberwiseClone();\n",
    "    public MintProfile Clone()\n"
    "    {\n"
    "        // Value-only clone: never copy PropertyChanged subscribers into realtime state.\n"
    "        var clone = new MintProfile();\n"
    "        clone.CopyFrom(this);\n"
    "        return clone;\n"
    "    }\n",
    "safe clone",
)
s = replace_once(
    s,
    "        AiAdaptation = source.AiAdaptation;\n",
    "        AiAdaptation = source.AiAdaptation;\n"
    "        AiNoiseMaxReductionDb = source.AiNoiseMaxReductionDb;\n"
    "        AiNoiseReductionDb = source.AiNoiseReductionDb;\n"
    "        AiNoiseSensitivity = source.AiNoiseSensitivity;\n"
    "        AiNoiseSpeechProtection = source.AiNoiseSpeechProtection;\n"
    "        AiNoiseLearnRate = source.AiNoiseLearnRate;\n",
    "copy noise fields",
)
write(path, s)


# -----------------------------------------------------------------------------
# Trained 12 -> 8 -> 4 compact noise specialist. Targets are bounded controls:
# suppression need, spectral-floor depth, speech protection, and noise learning.
# -----------------------------------------------------------------------------
path = "src/Cinder.MINT.App/Audio/AI/MintAiModels.cs"
s = read(path)
noise_model = r'''    internal static readonly NeuralModel Noise = new(
        12, 8, 4,
        [
            -0.00036436f, 0.09067614f, 0.00504667f, 0.01797737f, -0.01574378f, 0.07725794f, 0.12624504f, -0.10073345f,
            -0.00370419f, 0.00353233f, 0.00386851f, -0.00944289f, -0.00045819f, 0.01030841f, -0.00453072f, -0.01492845f,
            -0.00706185f, 0.07459890f, 0.00808560f, -0.09413856f, 0.00262960f, 0.17097095f, -0.02853801f, 0.02412856f,
            -0.00382197f, 0.00345226f, 0.00335470f, -0.01189810f, 0.00036677f, 0.01309791f, -0.00479390f, -0.01799552f,
            -0.00342764f, 0.00227691f, 0.00215642f, -0.01286745f, -0.00051921f, 0.01411949f, -0.00579399f, -0.01881978f,
            -0.00429478f, -0.07365767f, 0.00588613f, -0.07318122f, 0.00160150f, 0.08050275f, -0.01178279f, 0.01295284f,
            -0.00301672f, 0.00347341f, 0.00246186f, -0.01691620f, 0.00070200f, 0.01820302f, -0.00667925f, -0.02414670f,
            -0.15978937f, 0.03498833f, 0.27846944f, 0.09731723f, -0.01463548f, 0.08534905f, -0.23044659f, -0.15012367f,
            0.66152620f, -0.53222132f, 0.32058007f, 0.06533853f, 0.64006597f, -0.04178277f, 0.45596683f, -0.09966096f,
            -0.00451901f, -0.10420296f, 0.00754146f, -0.09771249f, 0.00214394f, 0.10747755f, -0.01379466f, 0.02571727f,
            0.00136084f, -0.15833065f, -0.00080609f, 0.01166726f, -0.00025345f, -0.09438024f, 0.01214354f, -0.05415550f,
            0.93164092f, 0.00860846f, 0.83896649f, 0.61850661f, -1.08495045f, 0.50954568f, -0.22882940f, 0.09443387f
        ],
        [
            0.14356764f, 0.28376514f, -0.15661798f, -0.29540685f, -0.51472098f, -0.50121915f, 0.18163791f, -0.64682597f
        ],
        [
            -0.29533520f, 0.23318288f, 0.00326815f, 0.67151994f, -0.84201276f, -0.46482441f, -0.20720567f, 0.14936838f,
            0.50995022f, 0.21916063f, 0.04919315f, 0.45905071f, -0.27445221f, 0.07790438f, 0.74968106f, -0.55558097f,
            0.10990295f, 0.32624426f, -0.03871041f, 0.79497784f, 0.26990318f, -0.27306947f, 0.46028960f, -0.46266681f,
            0.63603246f, 0.20778449f, -0.33790043f, -0.39021444f, 0.32076630f, -0.66613299f, -0.18723191f, 0.29056346f
        ],
        [
            0.54020834f, -0.14599884f, 0.43440139f, 0.43134835f
        ]);

'''
s = replace_once(s, "    internal static readonly NeuralModel Master = new(\n", noise_model + "    internal static readonly NeuralModel Master = new(\n", "noise model")
write(path, s)


# -----------------------------------------------------------------------------
# Runtime: map Noise to its own model and bounded denoiser controls.
# -----------------------------------------------------------------------------
path = "src/Cinder.MINT.App/Audio/AI/MintAiRuntime.cs"
s = read(path)
s = replace_once(
    s,
    "        MintAiSpecialist.Cleanup => MintAiModels.Cleanup,\n        MintAiSpecialist.Tone => MintAiModels.Tone,",
    "        MintAiSpecialist.Cleanup => MintAiModels.Cleanup,\n        MintAiSpecialist.Noise => MintAiModels.Noise,\n        MintAiSpecialist.Tone => MintAiModels.Tone,",
    "noise model mapping",
)
s = replace_once(
    s,
    "            MintAiSpecialist.Cleanup => ApplyCleanup(prediction, intent, runtime, strength, natural, maxCorrection, smoothing),\n            MintAiSpecialist.Tone => ApplyTone(prediction, runtime, strength, natural, maxCorrection, smoothing),",
    "            MintAiSpecialist.Cleanup => ApplyCleanup(prediction, intent, runtime, strength, natural, maxCorrection, smoothing),\n            MintAiSpecialist.Noise => ApplyNoise(prediction, intent, runtime, strength, natural, smoothing),\n            MintAiSpecialist.Tone => ApplyTone(prediction, runtime, strength, natural, maxCorrection, smoothing),",
    "noise action mapping",
)
noise_method = r'''    private static string ApplyNoise(
        float[] y,
        MintProfile intent,
        MintProfile p,
        float strength,
        float natural,
        float smoothing)
    {
        float severity = Clamp01(y[0]);
        float depth = Clamp01(y[1]);
        float speech = Clamp01(y[2]);
        float learning = Clamp01(y[3]);

        float maxReduction = Math.Clamp(intent.AiNoiseMaxReductionDb, 6f, 36f);
        float reductionTarget = Math.Clamp(
            maxReduction * severity * strength * (0.86f + (1f - natural) * 0.18f),
            0f,
            maxReduction);
        float sensitivityTarget = Math.Clamp(
            intent.AiNoiseSensitivity + (depth - 0.5f) * 0.34f,
            0.05f,
            1f);
        float protectionTarget = Math.Clamp(
            Math.Max(intent.AiNoiseSpeechProtection * 0.82f, speech * (0.64f + natural * 0.32f)),
            0f,
            1f);
        float learningTarget = Math.Clamp(
            Lerp(0.004f, 0.16f, learning) * Lerp(0.72f, 1.18f, intent.AiAdaptation),
            0.001f,
            0.25f);

        p.AiNoiseReductionDb = Smooth(p.AiNoiseReductionDb, reductionTarget, smoothing);
        p.AiNoiseSensitivity = Smooth(p.AiNoiseSensitivity, sensitivityTarget, smoothing);
        p.AiNoiseSpeechProtection = Smooth(p.AiNoiseSpeechProtection, protectionTarget, smoothing);
        p.AiNoiseLearnRate = Smooth(p.AiNoiseLearnRate, learningTarget, smoothing);

        return $"noise cut {p.AiNoiseReductionDb:F1} dB • sensitivity {p.AiNoiseSensitivity:P0} • voice protect {p.AiNoiseSpeechProtection:P0}";
    }

'''
s = replace_once(s, "    private static string ApplyCleanup(\n", noise_method + "    private static string ApplyCleanup(\n", "noise apply method")
write(path, s)


# -----------------------------------------------------------------------------
# Graph model: human-readable specialist identity plus noise first in voice/RVC.
# -----------------------------------------------------------------------------
path = "src/Cinder.MINT.App/Models/AudioGraphModel.cs"
s = read(path)
s = replace_once(
    s,
    "        MintAiSpecialist.Cleanup => \"noise • RVC artifacts • plosives • sibilance\",\n        MintAiSpecialist.Tone =>",
    "        MintAiSpecialist.Cleanup => \"RVC artifacts • plosives • sibilance • cleanup\",\n        MintAiSpecialist.Noise => \"neural spectral noise suppression • voice preservation\",\n        MintAiSpecialist.Tone =>",
    "noise subtitle",
)
old_voice = r'''        AudioNodeModel cleanup = graph.AddNode(AudioNodeType.AiProcessor, 270, 78);
        cleanup.AiSpecialist = MintAiSpecialist.Cleanup;
        cleanup.Profile.CopyFrom(MintProfiles.Voice["RVC Cleanup"]);

        AudioNodeModel voiceTone = graph.AddNode(AudioNodeType.AiProcessor, 510, 78);
        voiceTone.AiSpecialist = MintAiSpecialist.Tone;
        voiceTone.Profile.CopyFrom(MintProfiles.Voice["Natural Broadcast"]);

        AudioNodeModel voiceDynamics = graph.AddNode(AudioNodeType.AiProcessor, 750, 78);
        voiceDynamics.AiSpecialist = MintAiSpecialist.Dynamics;
        voiceDynamics.Profile.CopyFrom(MintProfiles.Voice["Streaming Strong"]);

        AudioNodeModel voiceLoudness = graph.AddNode(AudioNodeType.AiProcessor, 990, 78);
        voiceLoudness.AiSpecialist = MintAiSpecialist.Loudness;
        voiceLoudness.Profile.CopyFrom(MintProfiles.Voice["Natural Broadcast"]);
'''
new_voice = r'''        AudioNodeModel noise = graph.AddNode(AudioNodeType.AiProcessor, 270, 78);
        noise.AiSpecialist = MintAiSpecialist.Noise;
        noise.Profile.CopyFrom(MintProfiles.Voice["RVC Cleanup"]);
        noise.Profile.AiNoiseMaxReductionDb = 26f;
        noise.Profile.AiNoiseSensitivity = 0.72f;
        noise.Profile.AiNoiseSpeechProtection = 0.90f;

        AudioNodeModel cleanup = graph.AddNode(AudioNodeType.AiProcessor, 510, 78);
        cleanup.AiSpecialist = MintAiSpecialist.Cleanup;
        cleanup.Profile.CopyFrom(MintProfiles.Voice["RVC Cleanup"]);

        AudioNodeModel voiceTone = graph.AddNode(AudioNodeType.AiProcessor, 750, 78);
        voiceTone.AiSpecialist = MintAiSpecialist.Tone;
        voiceTone.Profile.CopyFrom(MintProfiles.Voice["Natural Broadcast"]);

        AudioNodeModel voiceDynamics = graph.AddNode(AudioNodeType.AiProcessor, 990, 78);
        voiceDynamics.AiSpecialist = MintAiSpecialist.Dynamics;
        voiceDynamics.Profile.CopyFrom(MintProfiles.Voice["Streaming Strong"]);

        AudioNodeModel voiceLoudness = graph.AddNode(AudioNodeType.AiProcessor, 1230, 78);
        voiceLoudness.AiSpecialist = MintAiSpecialist.Loudness;
        voiceLoudness.Profile.CopyFrom(MintProfiles.Voice["Natural Broadcast"]);
'''
s = replace_once(s, old_voice, new_voice, "default voice AI chain")
s = replace_once(s, "        AudioNodeModel mixer = graph.AddNode(AudioNodeType.Mixer, 1260, 190, \"STREAM BUS\");\n", "        AudioNodeModel mixer = graph.AddNode(AudioNodeType.Mixer, 1500, 190, \"STREAM BUS\");\n", "move mixer")
s = replace_once(s, "        AudioNodeModel master = graph.AddNode(AudioNodeType.AiProcessor, 1500, 190);\n", "        AudioNodeModel master = graph.AddNode(AudioNodeType.AiProcessor, 1740, 190);\n", "move master")
s = replace_once(s, "        AudioNodeModel output = graph.AddNode(AudioNodeType.Output, 1740, 190, \"STREAM OUTPUT\");\n", "        AudioNodeModel output = graph.AddNode(AudioNodeType.Output, 1980, 190, \"STREAM OUTPUT\");\n", "move output")
s = replace_once(
    s,
    "        Connect(graph, voice, \"OUT\", cleanup, \"IN\");\n        Connect(graph, cleanup, \"OUT\", voiceTone, \"IN\");",
    "        Connect(graph, voice, \"OUT\", noise, \"IN\");\n        Connect(graph, noise, \"OUT\", cleanup, \"IN\");\n        Connect(graph, cleanup, \"OUT\", voiceTone, \"IN\");",
    "wire noise before cleanup",
)
write(path, s)


# -----------------------------------------------------------------------------
# Palette: AI tasks are directly addable nodes, not a generic mystery box.
# -----------------------------------------------------------------------------
path = "src/Cinder.MINT.App/ViewModels/MainViewModel.cs"
s = read(path)
s = replace_once(
    s,
    "public sealed record NodePaletteItem(AudioNodeType Type, string Label)\n",
    "public sealed record NodePaletteItem(AudioNodeType Type, string Label, MintAiSpecialist? Specialist = null)\n",
    "palette record",
)
old_palette = r'''        NodePalette =
        [
            new(AudioNodeType.Input, "Audio input"),
            new(AudioNodeType.AiProcessor, "AI specialist"),
            new(AudioNodeType.Mixer, "Mix bus"),
            new(AudioNodeType.Ducker, "Sidechain ducker"),
            new(AudioNodeType.Output, "Audio output"),
            new(AudioNodeType.Gain, "Manual · gain / trim"),
            new(AudioNodeType.NoiseGate, "Manual · smart gate"),
            new(AudioNodeType.HighPass, "Manual · rumble cut"),
            new(AudioNodeType.DeEsser, "Manual · de-esser"),
            new(AudioNodeType.Equalizer, "Manual · equalizer"),
            new(AudioNodeType.LevelRider, "Manual · level rider"),
            new(AudioNodeType.Compressor, "Manual · compressor"),
            new(AudioNodeType.Limiter, "Manual · limiter")
        ];
'''
new_palette = r'''        NodePalette =
        [
            new(AudioNodeType.Input, "Audio input"),
            new(AudioNodeType.AiProcessor, "AI · noise filter", MintAiSpecialist.Noise),
            new(AudioNodeType.AiProcessor, "AI · cleanup / RVC repair", MintAiSpecialist.Cleanup),
            new(AudioNodeType.AiProcessor, "AI · tone", MintAiSpecialist.Tone),
            new(AudioNodeType.AiProcessor, "AI · dynamics", MintAiSpecialist.Dynamics),
            new(AudioNodeType.AiProcessor, "AI · loudness", MintAiSpecialist.Loudness),
            new(AudioNodeType.Mixer, "Mix bus"),
            new(AudioNodeType.AiProcessor, "AI · master", MintAiSpecialist.Master),
            new(AudioNodeType.Ducker, "Sidechain ducker"),
            new(AudioNodeType.Output, "Audio output"),
            new(AudioNodeType.Gain, "Manual · gain / trim"),
            new(AudioNodeType.NoiseGate, "Manual · smart gate"),
            new(AudioNodeType.HighPass, "Manual · rumble cut"),
            new(AudioNodeType.DeEsser, "Manual · de-esser"),
            new(AudioNodeType.Equalizer, "Manual · equalizer"),
            new(AudioNodeType.LevelRider, "Manual · level rider"),
            new(AudioNodeType.Compressor, "Manual · compressor"),
            new(AudioNodeType.Limiter, "Manual · limiter")
        ];
'''
s = replace_once(s, old_palette, new_palette, "AI node palette")
old_add = r'''            else if (node.Type == AudioNodeType.AiProcessor)
            {
                node.AiSpecialist = MintAiSpecialist.Cleanup;
                node.Profile.CopyFrom(MintProfiles.Voice["Natural Broadcast"]);
            }
'''
new_add = r'''            else if (node.Type == AudioNodeType.AiProcessor)
            {
                MintAiSpecialist specialist = SelectedPaletteItem.Specialist ?? MintAiSpecialist.Cleanup;
                node.AiSpecialist = specialist;

                if (specialist == MintAiSpecialist.Master)
                {
                    node.Profile.CopyFrom(MintProfiles.Program["Music Safe"]);
                    node.Profile.AiContentMode = MintAiContentMode.Mixed;
                    node.Profile.AiMaxCorrectionDb = 4f;
                }
                else
                {
                    node.Profile.CopyFrom(specialist is MintAiSpecialist.Noise or MintAiSpecialist.Cleanup
                        ? MintProfiles.Voice["RVC Cleanup"]
                        : MintProfiles.Voice["Natural Broadcast"]);
                }

                if (specialist == MintAiSpecialist.Noise)
                {
                    node.Profile.AiNoiseMaxReductionDb = 26f;
                    node.Profile.AiNoiseSensitivity = 0.72f;
                    node.Profile.AiNoiseSpeechProtection = 0.90f;
                }
            }
'''
s = replace_once(s, old_add, new_add, "add specialized AI node")
write(path, s)


# -----------------------------------------------------------------------------
# Inspector: simple noise intent surface. Generic AI telemetry stays shared.
# -----------------------------------------------------------------------------
path = "src/Cinder.MINT.App/MainWindow.xaml"
s = read(path)
anchor = '''                                        <TextBlock Text="INTENT — tell the brain what you want"\n                                                   Style="{StaticResource SectionLabel}"/>\n'''
noise_ui = r'''                                        <Border Background="#A30C2221"
                                                BorderBrush="{StaticResource AquaBrush}"
                                                BorderThickness="1"
                                                CornerRadius="14"
                                                Padding="13"
                                                Margin="0,0,0,12">
                                            <Border.Style>
                                                <Style TargetType="Border">
                                                    <Setter Property="Visibility" Value="Collapsed"/>
                                                    <Style.Triggers>
                                                        <DataTrigger Binding="{Binding SelectedNode.AiSpecialist}"
                                                                     Value="{x:Static models:MintAiSpecialist.Noise}">
                                                            <Setter Property="Visibility" Value="Visible"/>
                                                        </DataTrigger>
                                                    </Style.Triggers>
                                                </Style>
                                            </Border.Style>
                                            <StackPanel>
                                                <TextBlock Text="AI NOISE FILTER"
                                                           Foreground="{StaticResource AquaBrush}"
                                                           FontWeight="Black"/>
                                                <TextBlock Text="Neural noise analysis drives a deterministic 512-point spectral mask. No generated replacement audio."
                                                           TextWrapping="Wrap"
                                                           Style="{StaticResource TinyLabel}"
                                                           Margin="0,3,0,7"/>
                                                <TextBlock Text="Maximum noise reduction" Style="{StaticResource IntentLabel}"/>
                                                <Slider Minimum="6" Maximum="36"
                                                        Value="{Binding SelectedNode.Profile.AiNoiseMaxReductionDb}"/>
                                                <TextBlock Text="{Binding SelectedNode.Profile.AiNoiseMaxReductionDb, StringFormat={}{0:F0} dB max}"
                                                           HorizontalAlignment="Right"/>
                                                <TextBlock Text="Noise sensitivity" Style="{StaticResource IntentLabel}"/>
                                                <Slider Minimum="0.05" Maximum="1"
                                                        Value="{Binding SelectedNode.Profile.AiNoiseSensitivity}"/>
                                                <TextBlock Text="{Binding SelectedNode.Profile.AiNoiseSensitivity, StringFormat={}{0:P0}}"
                                                           HorizontalAlignment="Right"/>
                                                <TextBlock Text="Protect speech / RVC character" Style="{StaticResource IntentLabel}"/>
                                                <Slider Minimum="0" Maximum="1"
                                                        Value="{Binding SelectedNode.Profile.AiNoiseSpeechProtection}"/>
                                                <TextBlock Text="{Binding SelectedNode.Profile.AiNoiseSpeechProtection, StringFormat={}{0:P0}}"
                                                           HorizontalAlignment="Right"/>
                                            </StackPanel>
                                        </Border>

'''
s = replace_once(s, anchor, noise_ui + anchor, "noise inspector")
s = s.replace("Maximum tonal correction", "Maximum tonal correction (Tone / Cleanup / Master)")
write(path, s)


# -----------------------------------------------------------------------------
# Safety executable: include the new controls in bounded/finiteness checks and
# ensure the starter graph puts Noise before Cleanup, never after the mix.
# -----------------------------------------------------------------------------
path = "tests/Cinder.MINT.RoutingChecks/Program.cs"
s = read(path)
s = replace_once(
    s,
    "    (\"default AI master lives after explicit mix bus\", AiMasterFollowsExplicitMix)\n",
    "    (\"default AI master lives after explicit mix bus\", AiMasterFollowsExplicitMix),\n    (\"default AI noise filter precedes cleanup\", AiNoisePrecedesCleanup)\n",
    "noise topology check registration",
)
s = replace_once(
    s,
    "            AiContentMode = specialist == MintAiSpecialist.Cleanup ? MintAiContentMode.RvcVoice : MintAiContentMode.Auto,\n",
    "            AiContentMode = specialist is MintAiSpecialist.Cleanup or MintAiSpecialist.Noise ? MintAiContentMode.RvcVoice : MintAiContentMode.Auto,\n",
    "noise content mode in bounds test",
)
s = replace_once(
    s,
    "        Require(controlled.LimiterCeilingDb is >= -12f and <= -0.1f,\n            $\"{specialist} limiter ceiling escaped bounds.\");\n",
    "        Require(controlled.LimiterCeilingDb is >= -12f and <= -0.1f,\n            $\"{specialist} limiter ceiling escaped bounds.\");\n"
    "        Require(controlled.AiNoiseReductionDb is >= 0f and <= 36f, $\"{specialist} noise reduction escaped bounds.\");\n"
    "        Require(controlled.AiNoiseSensitivity is >= 0.05f and <= 1f, $\"{specialist} noise sensitivity escaped bounds.\");\n"
    "        Require(controlled.AiNoiseSpeechProtection is >= 0f and <= 1f, $\"{specialist} speech protection escaped bounds.\");\n"
    "        Require(controlled.AiNoiseLearnRate is >= 0.001f and <= 0.25f, $\"{specialist} noise learning rate escaped bounds.\");\n",
    "noise bounds",
)
noise_topology = r'''static void AiNoisePrecedesCleanup()
{
    AudioGraphModel graph = AudioGraphModel.CreateDefault();
    AudioNodeModel noise = graph.Nodes.Single(x => x.Type == AudioNodeType.AiProcessor && x.AiSpecialist == MintAiSpecialist.Noise);
    AudioNodeModel cleanup = graph.Nodes.Single(x => x.Type == AudioNodeType.AiProcessor && x.AiSpecialist == MintAiSpecialist.Cleanup);
    AudioConnectionModel incoming = graph.Incoming(cleanup).Single();
    AudioNodeModel source = graph.SourceNode(incoming) ?? throw new InvalidOperationException("AI Cleanup input source is missing.");

    Require(source.Id == noise.Id, "AI Noise Filter is not immediately upstream of AI Cleanup in the starter voice chain.");
}

'''
s = replace_once(s, "static bool AllFinite(MintProfile p) =>\n", noise_topology + "static bool AllFinite(MintProfile p) =>\n", "noise topology test")
s = replace_once(
    s,
    "    float.IsFinite(p.LimiterCeilingDb) &&\n    float.IsFinite(p.LimiterReleaseMs);\n",
    "    float.IsFinite(p.LimiterCeilingDb) &&\n    float.IsFinite(p.LimiterReleaseMs) &&\n    float.IsFinite(p.AiNoiseMaxReductionDb) &&\n    float.IsFinite(p.AiNoiseReductionDb) &&\n    float.IsFinite(p.AiNoiseSensitivity) &&\n    float.IsFinite(p.AiNoiseSpeechProtection) &&\n    float.IsFinite(p.AiNoiseLearnRate);\n",
    "noise finite controls",
)
write(path, s)


# -----------------------------------------------------------------------------
# Release pipeline version + notes.
# -----------------------------------------------------------------------------
path = ".github/workflows/build.yml"
s = read(path)
s = s.replace("v0.1.0-alpha.7", "v0.1.0-alpha.8")
s = s.replace("AI realtime/UI thread-isolation hotfix.", "Built-in AI Noise Filter preview.")
s = s.replace(
    "          Changed:\n",
    "          Changed:\n"
    "          - Added AI Noise Filter: a dedicated compact neural specialist controlling a deterministic low-latency spectral suppressor.\n"
    "          - 512-point sqrt-Hann overlap-add processing at 48 kHz with roughly 10.7 ms analysis latency.\n"
    "          - Noise node exposes simple maximum reduction, sensitivity, speech/RVC protection, strength, naturalness, and adaptation intent.\n"
    "          - Starter voice/RVC route now runs AI Noise Filter before AI Cleanup, Tone, Dynamics, and Loudness.\n"
    "          - AI node shelf now exposes each specialist directly instead of only a generic AI box.\n"
)
write(path, s)

print("AI noise filter patch applied")
