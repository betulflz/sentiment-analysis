using System.Runtime.InteropServices;

namespace SentimentAnalysis.Infrastructure.Audio;

/// <summary>macOS AudioToolbox / AudioQueue C API'sinin P/Invoke yüzeyi. Native tipler
/// yalnızca burada ve <see cref="MicrophoneCaptureSource"/> içinde yaşar (motor sınırı).
/// İmzalar gerçek donanımda spike ile doğrulandı (bkz. 08d-mikrofon-spike-olcumu.md).</summary>
internal static class AudioQueueInterop
{
    internal const string Library =
        "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    internal const uint FormatLinearPcm = 0x6C70636D;  // 'lpcm'
    internal const uint FlagsSignedIntegerPacked = 0xC; // SignedInteger | IsPacked

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioStreamBasicDescription
    {
        public double mSampleRate;
        public uint mFormatID;
        public uint mFormatFlags;
        public uint mBytesPerPacket;
        public uint mFramesPerPacket;
        public uint mBytesPerFrame;
        public uint mChannelsPerFrame;
        public uint mBitsPerChannel;
        public uint mReserved;
    }

    /// <summary>Alan ofsetleri 64-bit hizalamayla native struct'a birebir oturur
    /// (spike'ta <c>mAudioDataByteSize</c> doğru okunarak doğrulandı).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioQueueBuffer
    {
        public uint mAudioDataBytesCapacity;
        public IntPtr mAudioData;
        public uint mAudioDataByteSize;
        public IntPtr mUserData;
        public uint mPacketDescriptionCapacity;
        public IntPtr mPacketDescriptions;
        public uint mPacketDescriptionCount;
    }

    internal delegate void InputCallback(
        IntPtr userData, IntPtr audioQueue, IntPtr buffer,
        IntPtr startTime, uint numPackets, IntPtr packetDescs);

    [DllImport(Library)]
    internal static extern int AudioQueueNewInput(
        ref AudioStreamBasicDescription format, InputCallback callback, IntPtr userData,
        IntPtr runLoop, IntPtr runLoopMode, uint flags, out IntPtr audioQueue);

    [DllImport(Library)]
    internal static extern int AudioQueueAllocateBuffer(
        IntPtr audioQueue, uint bufferByteSize, out IntPtr buffer);

    [DllImport(Library)]
    internal static extern int AudioQueueEnqueueBuffer(
        IntPtr audioQueue, IntPtr buffer, uint numPacketDescs, IntPtr packetDescs);

    [DllImport(Library)]
    internal static extern int AudioQueueStart(IntPtr audioQueue, IntPtr startTime);

    [DllImport(Library)]
    internal static extern int AudioQueueStop(IntPtr audioQueue, bool immediate);

    [DllImport(Library)]
    internal static extern int AudioQueueDispose(IntPtr audioQueue, bool immediate);
}
