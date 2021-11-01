using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace MarieAmp
{
    public class StreamMP3Reader: ObservableObject
    {
        public StreamMP3Reader()
        {

        }

        private double _dBuffer_second;

        public double m_dBuffer_second
        {
            get { return _dBuffer_second; }
            set { _dBuffer_second = value; NotifyPropertyChanged(); }
        }


        public float m_Volume { get; set; } = 0.5F;

        private void _timer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (_playbackState != StreamingPlaybackState.Stopped)
            {
                if (_waveOut == null && _bufferedWaveProvider != null)
                {
                    Debug.WriteLine("Creating WaveOut Device");
                    _waveOut = new WaveOut();
                    //_waveOut.PlaybackStopped += OnPlaybackStopped;
                    _volumeProvider = new VolumeWaveProvider16(_bufferedWaveProvider);
                    _volumeProvider.Volume = m_Volume;
                    _waveOut.Init(_volumeProvider);
                    int iMax = (int)_bufferedWaveProvider.BufferDuration.TotalMilliseconds;
                    //progressBarBuffer.Maximum = (int)bufferedWaveProvider.BufferDuration.TotalMilliseconds;
                }
                else if (_bufferedWaveProvider != null)
                {
                    m_dBuffer_second = _bufferedWaveProvider.BufferedDuration.TotalSeconds;
                    //ShowBufferState(bufferedSeconds);
                    // make it stutter less if we buffer up a decent amount before playing
                    if (m_dBuffer_second < 0.5 && _playbackState == StreamingPlaybackState.Playing && !_fullyDownloaded)
                    {
                        Pause();
                    }
                    else if (m_dBuffer_second > 4 && _playbackState == StreamingPlaybackState.Buffering)
                    {
                        Play();
                    }
                    else if (_fullyDownloaded && m_dBuffer_second == 0)
                    {
                        Debug.WriteLine("Reached end of stream");
                        //StopPlayback();
                    }
                }

            }
        }

        enum StreamingPlaybackState
        {
            Stopped,
            Playing,
            Buffering,
            Paused
        }

        private static System.Timers.Timer _timer;
        private HttpWebRequest _webRequest;
        private BufferedWaveProvider _bufferedWaveProvider;
        private volatile bool _fullyDownloaded;
        private bool _bEnd=false;
        private volatile StreamingPlaybackState _playbackState= StreamingPlaybackState.Stopped;
        private IWavePlayer _waveOut;
        private volatile bool fullyDownloaded;
        private VolumeWaveProvider16 _volumeProvider;

        private static IMp3FrameDecompressor CreateFrameDecompressor(Mp3Frame frame)
        {
            WaveFormat waveFormat = new Mp3WaveFormat(frame.SampleRate, frame.ChannelMode == ChannelMode.Mono ? 1 : 2,
                frame.FrameLength, frame.BitRate);
            return new AcmMp3FrameDecompressor(waveFormat);
        }
        public void streamMe(string url)
        {
            if (_playbackState == StreamingPlaybackState.Stopped)
            {
                _playbackState = StreamingPlaybackState.Buffering;
                _bufferedWaveProvider = null;
                ThreadPool.QueueUserWorkItem(StreamMp3, url);
                _timer = new System.Timers.Timer();
                _timer.Elapsed += _timer_Elapsed;
                _timer.Interval = 1000;
                _timer.AutoReset = true;
                _timer.Enabled = true;
                //timer1.Enabled = true;
            }
            else if (_playbackState == StreamingPlaybackState.Paused)
            {
                _playbackState = StreamingPlaybackState.Buffering;
            }
        }

        private bool IsBufferNearlyFull
        {
            get
            {
                return _bufferedWaveProvider != null &&
                       _bufferedWaveProvider.BufferLength - _bufferedWaveProvider.BufferedBytes
                       < _bufferedWaveProvider.WaveFormat.AverageBytesPerSecond / 4;
            }
        }

        private void StreamMp3(object state)
        {
            _fullyDownloaded = false;
            var url = (string)state;
            _webRequest = (HttpWebRequest)WebRequest.Create(url);
            HttpWebResponse resp;
            try
            {
                resp = (HttpWebResponse)_webRequest.GetResponse();
            }
            catch (WebException e)
            {
                if (e.Status != WebExceptionStatus.RequestCanceled)
                {
                    //ShowError(e.Message);
                }
                return;
            }
            var buffer = new byte[16384 * 4]; // needs to be big enough to hold a decompressed frame

            IMp3FrameDecompressor decompressor = null;
            try
            {
                using (var responseStream = resp.GetResponseStream())
                {
                    var readFullyStream = new ReadFullyStream(responseStream);
                    do
                    {
                        if (IsBufferNearlyFull)
                        {
                            Debug.WriteLine("Buffer getting full, taking a break");
                            Thread.Sleep(500);
                        }
                        else
                        {
                            Mp3Frame frame;
                            try
                            {
                                frame = Mp3Frame.LoadFromStream(readFullyStream);
                            }
                            catch (EndOfStreamException)
                            {
                                _fullyDownloaded = true;
                                // reached the end of the MP3 file / stream
                                break;
                            }
                            catch (WebException)
                            {
                                // probably we have aborted download from the GUI thread
                                break;
                            }
                            if (frame == null) break;
                            if (decompressor == null)
                            {
                                // don't think these details matter too much - just help ACM select the right codec
                                // however, the buffered provider doesn't know what sample rate it is working at
                                // until we have a frame
                                decompressor = CreateFrameDecompressor(frame);
                                _bufferedWaveProvider = new BufferedWaveProvider(decompressor.OutputFormat);
                                _bufferedWaveProvider.BufferDuration =
                                    TimeSpan.FromSeconds(20); // allow us to get well ahead of ourselves
                                //this.bufferedWaveProvider.BufferedDuration = 250;
                            }
                            int decompressed = decompressor.DecompressFrame(frame, buffer, 0);
                            Debug.WriteLine(String.Format("Decompressed a frame {0}", decompressed));
                            _bufferedWaveProvider.AddSamples(buffer, 0, decompressed);
                        }

                    } while (_playbackState != StreamingPlaybackState.Stopped);
                    Debug.WriteLine("Exiting");
                    // was doing this in a finally block, but for some reason
                    // we are hanging on response stream .Dispose so never get there
                    decompressor.Dispose();
                }
            }
            finally
            {
                if (decompressor != null)
                {
                    decompressor.Dispose();
                }
            }
        }

        public void Stop()
        {
            StopPlayback();
        }

        private void Pause()
        {
            _playbackState = StreamingPlaybackState.Buffering;
            _waveOut.Pause();
            Debug.WriteLine(String.Format("Paused to buffer, waveOut.PlaybackState={0}", _waveOut.PlaybackState));
        }

        private void Play()
        {
            _waveOut.Play();
            Debug.WriteLine(String.Format("Started playing, waveOut.PlaybackState={0}", _waveOut.PlaybackState));
            _playbackState = StreamingPlaybackState.Playing;
        }


        private void StopPlayback()
        {
            if (_playbackState != StreamingPlaybackState.Stopped)
            {
                if (!fullyDownloaded)
                {
                    _webRequest.Abort();
                }

                _playbackState = StreamingPlaybackState.Stopped;
                if (_waveOut != null)
                {
                    _waveOut.Stop();
                    _waveOut.Dispose();
                    _waveOut = null;
                }
                _timer.Enabled = false;
                // n.b. streaming thread may not yet have exited
                Thread.Sleep(500);
                
            }
        }
    }
}
