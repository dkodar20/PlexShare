/// <author>Mayank Singla</author>
/// <summary>
/// Defines the "SharedClientScreen" class which represents the screen
/// shared by a client
/// </summary>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;

// Each frame consists of the resolution of the image and the ImageDiffList
using Frame = System.Tuple<System.Tuple<int, int>,
                        System.Collections.Generic.List<System.Tuple<System.Tuple<int, int>,
                        System.Tuple<int, int, int>>>>;

using Timer = System.Timers.Timer;

namespace PlexShareScreenshare.Server
{
    /// <summary>
    /// Represents the screen shared by a client
    /// </summary>
    public class SharedClientScreen
    {
        /// <summary>
        /// The timeout value in "milliseconds" defining the timeout for
        /// the timer in SharedClientScreen which represents the maximum time
        /// to wait for the arrival of the packet with the CONFIRMATION header
        /// </summary>
        public static readonly double Timeout = 5000;

        private readonly ITimerManager _server;

        // The screen stitcher object
        private readonly ScreenStitcher _stitcher;

        // The images received from the clients as packets
        private readonly Queue<Frame> _imageQueue;

        // The images which will be received after patching the previous
        // screen image of the client with the new image and
        // ready to be displayed
        private readonly Queue<Bitmap> _finalImageQueue;

        // Timer which keeps track of the time the confirmation packet was
        // received that the client is presenting the screen
        private readonly Timer _timer;

        // Token and its source for killing the task
        private CancellationTokenSource? _source;

        private Task? _imageSendTask;

        // Initialize the ScreenSticher object
        // Initialize the timer
        public SharedClientScreen(string clientId, string clientName, ITimerManager server)
        {
            this.Id = clientId;
            this.Name = clientName;
            _server = server ?? throw new ArgumentNullException(nameof(server));
            _stitcher = new ScreenStitcher(this);
            _imageQueue = new Queue<Frame>();
            _finalImageQueue = new Queue<Bitmap>();
            _imageSendTask = null;
            this.CurrentImage = null;
            this.Pinned = false;

            _timer = new Timer();
            _timer.Elapsed += new ElapsedEventHandler((sender, e) => _server.OnTimeOut(sender, e, Id));
            _timer.AutoReset = false;
            this.UpdateTimer();
            _timer.Enabled = true;
        }

        // The ID of the client sharing this screen
        public string Id { get; private set; }

        // The name of the client sharing this screen
        public string Name { get; private set; }

        // The current screen image of the client displayed
        public Bitmap? CurrentImage { get; set; }

        // Whether the client is pinned or not
        public bool Pinned { get; set; }

        // Pops and returns the image from the `_imageQueue`
        public Frame? GetImage()
        {
            try
            {
                return _imageQueue.Dequeue();
            }
            catch (Exception e)
            {
                Trace.WriteLine($"[SharedClientScreen::GetImage()] Dequeue failed: {e.Message}");
                return null;
            }
        }

        // Insert the image into the `_imageQueue`
        public void PutImage(Frame frame)
        {
            _imageQueue.Enqueue(frame);
        }

        // Pops and returns the image from the `_finalImageQueue`
        public Bitmap? GetFinalImage()
        {
            try
            {
                return _finalImageQueue.Dequeue();
            }
            catch (Exception e)
            {
                Trace.WriteLine($"[SharedClientScreen::GetFinalImage()] Dequeue failed: {e.Message}");
                return null;
            }
        }

        // Insert the image into the `_finalImageQueue`
        public void PutFinalImage(Bitmap image)
        {
            _finalImageQueue.Enqueue(image);
        }

        // Calls `sticher.StartStiching`
        // Create (if not exist) and start the task `ImageSendTask` with the lambda function
        // The `CurrentImage` variable will be bind to the xaml file
        public void StartProcessing(Action<CancellationToken> fun)
        {
            _stitcher?.StartStitching();

            _source = new CancellationTokenSource();
            _imageSendTask = new Task(() => fun(_source.Token), _source.Token);
            _imageSendTask.Start();
        }

        // Calls `sticher.StopStiching`
        // Kills the task `ImageSendTask` and mark it as null
        // Empty both the queues
        public void StopProcessing()
        {
            _stitcher.StopStitching();
            _source?.Cancel();
            _imageQueue.Clear();
            _finalImageQueue.Clear();
            _imageSendTask = null;
        }

        public void UpdateTimer()
        {
            _timer.Interval = Timeout;
        }
    }
}
