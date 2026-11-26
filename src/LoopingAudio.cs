using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace SoulsRevive
{
    // Each soundscape owns a separate device stream. Stopping a preview cannot
    // stop the companion's music or interfere with Windows notification sounds.
    sealed class LoopingAudio : IDisposable
    {
        IntPtr device, samples, header;
        bool prepared;
        readonly uint headerSize = (uint)Marshal.SizeOf<WaveHeader>();

        public LoopingAudio(byte[] wave)
        {
            if (wave.Length < 44 || Encoding.ASCII.GetString(wave, 0, 4) != "RIFF"
                || Encoding.ASCII.GetString(wave, 36, 4) != "data")
                throw new InvalidDataException("Invalid soundscape audio.");
            var format = new WaveFormat {
                Format = 1, Channels = 1, SampleRate = 16000,
                BytesPerSecond = 32000, BlockAlign = 2, BitsPerSample = 16
            };
            try
            {
                Check(waveOutOpen(out device, uint.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0));
                int length = wave.Length - 44;
                samples = Marshal.AllocHGlobal(length);
                Marshal.Copy(wave, 44, samples, length);
                header = Marshal.AllocHGlobal((int)headerSize);
                Marshal.StructureToPtr(new WaveHeader { Data = samples, Length = (uint)length }, header, false);
                Check(waveOutPrepareHeader(device, header, headerSize));
                prepared = true;
                var buffer = Marshal.PtrToStructure<WaveHeader>(header);
                buffer.Flags |= 4 | 8; // WHDR_BEGINLOOP | WHDR_ENDLOOP, single PCM block.
                buffer.Loops = uint.MaxValue;
                Marshal.StructureToPtr(buffer, header, false);
                Check(waveOutWrite(device, header, headerSize));
            }
            catch { Release(); throw; }
        }

        internal uint PlaybackBytes
        {
            get
            {
                if (device == IntPtr.Zero) return 0;
                var time = new WaveTime { Type = 4 }; // TIME_BYTES
                Check(waveOutGetPosition(device, ref time, (uint)Marshal.SizeOf<WaveTime>()));
                if (time.Type == 2) return time.Value * 2;
                if (time.Type == 1) return time.Value * 32;
                return time.Value;
            }
        }

        // Dispose never throws: a failing driver call must not leave the device open or leak buffers.
        public void Dispose() { Release(); GC.SuppressFinalize(this); }
        ~LoopingAudio() { Release(); }

        void Release()
        {
            if (device != IntPtr.Zero)
            {
                _ = waveOutReset(device);
                if (prepared) { _ = waveOutUnprepareHeader(device, header, headerSize); prepared = false; }
                _ = waveOutClose(device);
                device = IntPtr.Zero;
            }
            if (header != IntPtr.Zero) { Marshal.FreeHGlobal(header); header = IntPtr.Zero; }
            if (samples != IntPtr.Zero) { Marshal.FreeHGlobal(samples); samples = IntPtr.Zero; }
        }

        static void Check(uint result)
        {
            if (result == 0) return;
            var message = new StringBuilder(256);
            _ = waveOutGetErrorText(result, message, message.Capacity);
            throw new InvalidOperationException(message.ToString());
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        struct WaveFormat
        {
            public ushort Format, Channels;
            public uint SampleRate, BytesPerSecond;
            public ushort BlockAlign, BitsPerSample, ExtraSize;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct WaveHeader
        {
            public IntPtr Data;
            public uint Length, BytesRecorded;
            public UIntPtr User;
            public uint Flags, Loops;
            public IntPtr Next;
            public UIntPtr Reserved;
        }
        [StructLayout(LayoutKind.Explicit, Size = 12)]
        struct WaveTime
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(4)] public uint Value;
        }
        [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr device, uint id, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr device);
        [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr device, IntPtr header, uint size);
        [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr device);
        [DllImport("winmm.dll")] static extern uint waveOutGetPosition(IntPtr device, ref WaveTime time, uint size);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern uint waveOutGetErrorText(uint error, StringBuilder text, int size);
    }
}
