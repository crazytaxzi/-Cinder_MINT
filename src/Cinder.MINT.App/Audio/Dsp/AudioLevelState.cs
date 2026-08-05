namespace Cinder.MINT.Audio.Dsp;

public sealed class AudioLevelState
{
    private float _voicePeakDb = -90;
    private float _programPeakDb = -90;
    private float _masterPeakDb = -90;
    private float _voiceActivity;

    public float VoicePeakDb
    {
        get => Volatile.Read(ref _voicePeakDb);
        set => Volatile.Write(ref _voicePeakDb, value);
    }

    public float ProgramPeakDb
    {
        get => Volatile.Read(ref _programPeakDb);
        set => Volatile.Write(ref _programPeakDb, value);
    }

    public float MasterPeakDb
    {
        get => Volatile.Read(ref _masterPeakDb);
        set => Volatile.Write(ref _masterPeakDb, value);
    }

    public float VoiceActivity
    {
        get => Volatile.Read(ref _voiceActivity);
        set => Volatile.Write(ref _voiceActivity, value);
    }
}
