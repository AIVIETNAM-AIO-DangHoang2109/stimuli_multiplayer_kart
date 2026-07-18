using UnityEngine;

namespace Extra.VoiceChat
{
    [System.Serializable]
    public class VoiceChatSettings
    {
        [Tooltip("Recording sample rate in Hz. Speech quality (16000) keeps files small.")]
        public int SampleRate = 16000;

        [Tooltip("Duration of each saved WAV segment in seconds.")]
        public float SegmentDurationSeconds = 60f;

        [Tooltip("Number of channels (1 = Mono, 2 = Stereo). Mono is recommended for speech.")]
        public int ChannelCount = 1;

        [Tooltip("Relative project folder or absolute path where WAV files are saved.")]
        public string OutputFolderPath = "Assets/Extra/Resources/VoiceChat";

        [Tooltip("Prefix for the saved file names.")]
        public string FileNamePrefix = "voice_";

        [Tooltip("Override microphone device name. Leave empty to use the system default device.")]
        public string MicrophoneDeviceName = "";

        [Tooltip("Start recording immediately when the manager initializes.")]
        public bool AutoStartOnAwake = true;

        [Tooltip("Length of the internal looping microphone AudioClip buffer in seconds.")]
        public int MaxMicBufferLengthSeconds = 120;

        [Tooltip("Toggle verbose debug logs in the Unity Editor console.")]
        public bool EnableDebugLogs = false;
    }
}
