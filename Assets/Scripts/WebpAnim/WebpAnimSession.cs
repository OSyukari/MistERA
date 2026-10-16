using System;
using System.IO;
using System.Runtime.InteropServices;
using unity.libwebp;
using unity.libwebp.Interop;

namespace MistEra.Webp
{
    /// <summary>
    /// One native libwebp animation decoder (WebPAnimDecoder) over a pinned copy of the compressed file.
    /// Frames are composited by libwebp (blend/dispose handled) into a single canvas it owns, so memory is
    /// the compressed file + one RGBA canvas, regardless of frame count.
    /// Not thread-safe: the owner must keep at most one DecodeNext in flight and never Dispose during one.
    /// Touches no Unity API, so it can be opened and decoded on a worker thread.
    /// </summary>
    public sealed unsafe class WebpAnimSession : IDisposable
    {
        public struct Frame
        {
            /// <summary>Top-down RGBA32 canvas (non-premultiplied). Valid only until the next DecodeNext / Dispose.</summary>
            public IntPtr Pixels;
            /// <summary>End time of this frame in ms since the loop start (libwebp timestamps are cumulative).</summary>
            public int Timestamp;
            /// <summary>The decoder rewound to the first frame before producing this one.</summary>
            public bool Wrapped;
            /// <summary>This is the last frame of the animation.</summary>
            public bool IsLast;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int FrameCount { get; private set; }
        /// <summary>Canvas size in bytes (Width * Height * 4).</summary>
        public int ByteSize { get; private set; }

        private byte[] _bytes;
        private GCHandle _pin;
        private WebPAnimDecoder* _dec;

        public static WebpAnimSession Open(string fullPath)
        {
            return new WebpAnimSession(File.ReadAllBytes(fullPath));
        }

        public WebpAnimSession(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) throw new ArgumentException("empty webp data");

            // the demuxer keeps pointing into the input, so it stays pinned for the decoder's lifetime
            _bytes = bytes;
            _pin = GCHandle.Alloc(_bytes, GCHandleType.Pinned);

            WebPData data = new WebPData
            {
                bytes = (byte*)_pin.AddrOfPinnedObject(),
                size = new UIntPtr((uint)_bytes.Length)
            };

            WebPAnimDecoderOptions options = default;
            NativeLibwebpdemux.WebPAnimDecoderOptionsInit(&options);
            options.color_mode = WEBP_CSP_MODE.MODE_RGBA;
            options.use_threads = 1;

            _dec = NativeLibwebpdemux.WebPAnimDecoderNew(&data, &options);
            if (_dec == null)
            {
                Dispose();
                throw new InvalidDataException("not a decodable webp");
            }

            WebPAnimInfo info = default;
            if (NativeLibwebpdemux.WebPAnimDecoderGetInfo(_dec, &info) == 0)
            {
                Dispose();
                throw new InvalidDataException("cannot read webp animation info");
            }

            Width = (int)info.canvas_width;
            Height = (int)info.canvas_height;
            FrameCount = (int)info.frame_count;
            ByteSize = Width * Height * 4;
        }

        /// <summary>
        /// Decodes the next frame into the canvas, rewinding to the first frame after the last one.
        /// Returns false on a decode error or after Dispose.
        /// </summary>
        public bool DecodeNext(out Frame frame)
        {
            frame = default;
            if (_dec == null) return false;

            if (NativeLibwebpdemux.WebPAnimDecoderHasMoreFrames(_dec) == 0)
            {
                NativeLibwebpdemux.WebPAnimDecoderReset(_dec);
                frame.Wrapped = true;
            }

            byte* buf = null;
            int timestamp = 0;
            if (NativeLibwebpdemux.WebPAnimDecoderGetNext(_dec, &buf, &timestamp) == 0) return false;

            frame.Pixels = (IntPtr)buf;
            frame.Timestamp = timestamp;
            frame.IsLast = NativeLibwebpdemux.WebPAnimDecoderHasMoreFrames(_dec) == 0;
            return true;
        }

        public void Dispose()
        {
            if (_dec != null)
            {
                NativeLibwebpdemux.WebPAnimDecoderDelete(_dec);
                _dec = null;
            }
            if (_pin.IsAllocated) _pin.Free();
            _bytes = null;
        }
    }
}
