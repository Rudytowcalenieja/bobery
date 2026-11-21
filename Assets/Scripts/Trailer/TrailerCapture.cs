#if UNITY_EDITOR
using System.Collections;
using System.IO;
using Unity.Collections;
using UnityEditor.Media;
using UnityEngine;

namespace Bobery.Trailer
{
    /// <summary>
    /// Editor-only movie writer: game view (with overlay UI) + game audio → H.264/AAC .mp4.
    /// Runs the game at a fixed frame rate (Time.captureFramerate) and renders audio in lock-step
    /// (AudioRenderer), so picture and sound stay in sync however slowly the Editor renders.
    /// Frames are written only while <see cref="Recording"/> is on, so the game can run "off camera"
    /// between shots (hard cuts).
    /// </summary>
    public class TrailerCapture : MonoBehaviour
    {
        public const int Fps = 30;
        public int width = 1920, height = 1080;

        public bool Recording { get; set; }
        public int FramesWritten { get; private set; }
        public float VideoTime => FramesWritten / (float)Fps;
        public string OutputPath { get; private set; }

        /// <summary>Folder for occasional stills (for checking shots without a video player). Null = none.</summary>
        public string StillsFolder;
        public float stillEvery = 1f;

        MediaEncoder _encoder;
        RenderTexture _rt;
        Texture2D _frame;
        int _channels;
        bool _audio;

        public void Begin(string path)
        {
            OutputPath = path;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path)) File.Delete(path);

            Time.captureFramerate = Fps;
            _channels = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            int rate = AudioSettings.outputSampleRate;

            var video = new VideoTrackAttributes
            {
                frameRate = new MediaRational(Fps),
                width = (uint)width,
                height = (uint)height,
                includeAlpha = false,
                bitRateMode = UnityEditor.VideoBitrateMode.High,
            };
            var audio = new AudioTrackAttributes
            {
                sampleRate = new MediaRational(rate),
                channelCount = (ushort)_channels,
                language = "en",
            };
            _encoder = new MediaEncoder(path, video, audio);
            _audio = AudioRenderer.Start();

            _rt = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            _frame = new Texture2D(width, height, TextureFormat.RGBA32, false);
            if (!string.IsNullOrEmpty(StillsFolder)) Directory.CreateDirectory(StillsFolder);
            StartCoroutine(CaptureLoop());
        }

        IEnumerator CaptureLoop()
        {
            var eof = new WaitForEndOfFrame();
            while (_encoder != null)
            {
                yield return eof;
                if (_encoder == null) yield break;

                // Audio for this frame (always drained, kept only while recording).
                if (_audio)
                {
                    int n = AudioRenderer.GetSampleCountForCaptureFrame();
                    if (n > 0)
                    {
                        var buf = new NativeArray<float>(n * _channels, Allocator.Temp);
                        AudioRenderer.Render(buf);
                        if (Recording) _encoder.AddSamples(buf);
                        buf.Dispose();
                    }
                }
                if (!Recording) continue;

                var shot = ScreenCapture.CaptureScreenshotAsTexture();
                Graphics.Blit(shot, _rt);
                Destroy(shot);
                var prev = RenderTexture.active;
                RenderTexture.active = _rt;
                _frame.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                _frame.Apply(false);
                RenderTexture.active = prev;
                _encoder.AddFrame(_frame);

                if (!string.IsNullOrEmpty(StillsFolder) && FramesWritten % Mathf.Max(1, Mathf.RoundToInt(stillEvery * Fps)) == 0)
                    File.WriteAllBytes(Path.Combine(StillsFolder, $"t{VideoTime:00.0}.jpg"), _frame.EncodeToJPG(80));
                FramesWritten++;
            }
        }

        public void End()
        {
            Recording = false;
            if (_audio) AudioRenderer.Stop();
            _audio = false;
            _encoder?.Dispose();
            _encoder = null;
            Time.captureFramerate = 0;
            if (_rt) _rt.Release();
        }

        void OnDestroy() => End();
    }
}
#endif
