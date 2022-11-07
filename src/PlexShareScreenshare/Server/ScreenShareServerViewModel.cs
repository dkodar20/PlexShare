using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace PlexShareScreenshare.Server
{
    internal class ScreenShareServerViewModel : INotifyPropertyChanged, IMessageListener
    {
        // The maximum number of tiles of the shared screens
        // on a single page that will be shown to the server
        public static int MaxTiles = 9;

        public static List<(int Row, int Column)> NumRowsColumns = new()
        {
            (1, 1),  // 0 Total Screen
            (1, 1),  // 1 Total Screen
            (1, 2),  // 2 Total Screens
            (1, 3),  // 3 Total Screens
            (2, 2),  // 4 Total Screens
            (2, 3),  // 5 Total Screens
            (2, 3),  // 6 Total Screens
            (3, 3),  // 7 Total Screens
            (3, 3),  // 8 Total Screens
            (3, 3)   // 9 Total Screens
        };

        public static List<(int Height, int Width)> Resolution = new()
        {
            (0, 0),      // 0 Total Screen
            (100, 100),  // 1 Total Screen
            (100, 100),  // 2 Total Screens
            (100, 100),  // 3 Total Screens
            (100, 100),  // 4 Total Screens
            (100, 100),  // 5 Total Screens
            (100, 100),  // 6 Total Screens
            (100, 100),  // 7 Total Screens
            (100, 100),  // 8 Total Screens
            (100, 100)   // 9 Total Screens
        };

        // Underlying data model
        private readonly ScreenShareServer _model;

        // The current page number
        private int _currentPage;

        private List<SharedClientScreen> _subscribers;

        // Called by `.xaml.cs`
        // Initializes the `ScreenShareServer` model
        public ScreenShareServerViewModel()
        {
            _model = ScreenShareServer.GetInstance(this);
            _currentPage = 1;
            _subscribers = new List<SharedClientScreen>();
            CurrentWindowClients = new ObservableCollection<SharedClientScreen>();
            (CurrentPageRows, CurrentPageColumns) = NumRowsColumns[_currentPage];
            CurrentPageResolution = Resolution[_currentPage];
        }

        // Property changed event raised when a property is changed on a component
        public event PropertyChangedEventHandler? PropertyChanged;

        public void OnSubscribersChanged(List<SharedClientScreen> subscribers)
        {
            List<SharedClientScreen> sortedSubscribers = subscribers.OrderBy(subscriber => subscriber.Name).ToList();
            _subscribers = MovePinnedSubscribers(sortedSubscribers);
            RecomputeCurrentWindowClients();
        }

        // For each client in the currently active window, it will start a
        // separate thread in which the final processed images of clients
        // will be dequeued and sent to the view
        public ObservableCollection<SharedClientScreen> CurrentWindowClients { get; private set; }

        // Keeps track of the current page that the server is viewing
        public int CurrentPage
        {
            get => _currentPage;

            set
            {
                _ = this.ApplicationMainThreadDispatcher.BeginInvoke(
                        DispatcherPriority.Normal,
                        new Action<int>((newPage) =>
                        {
                            lock (this)
                            {
                                // Note that Bitmap cannot be automatically marshaled to the main thread
                                // if it were created on the worker thread. Hence the data model just passes
                                // the path to the image, and the main thread creates an image from it.

                                this._currentPage = newPage;

                                this.OnPropertyChanged(nameof(this.CurrentPage));
                            }
                        }),
                        value);

                RecomputeCurrentWindowClients();
                // Update the field `_currentPage`
                // Recompute the field `currWinClients` using the pagination logic
            }
        }

        public int CurrentPageRows { get; private set; }

        public int CurrentPageColumns { get; private set; }

        public (int, int) CurrentPageResolution { get; private set; }

        /// <summary>
        /// Gets the dispatcher to the main thread. In case it is not available
        /// (such as during unit testing) the dispatcher associated with the
        /// current thread is returned.
        /// </summary>
        private Dispatcher ApplicationMainThreadDispatcher =>
            (Application.Current?.Dispatcher != null) ?
                    Application.Current.Dispatcher :
                    Dispatcher.CurrentDispatcher;

        private static List<SharedClientScreen> MovePinnedSubscribers(List<SharedClientScreen> sharedClients)
        {
            List<SharedClientScreen> pinnedClients = new();
            List<SharedClientScreen> unpinnedClients = new();

            foreach (var sharedClientScreen in sharedClients)
            {
                if (sharedClientScreen.Pinned)
                {
                    pinnedClients.Add(sharedClientScreen);
                }
                else
                {
                    unpinnedClients.Add(sharedClientScreen);
                }
            }

            return pinnedClients.Concat(unpinnedClients).ToList();
        }

        // Recompute current window clients based on pagination logic
        // Notify the subscribers
        public void RecomputeCurrentWindowClients()
        {
            int totalCount = _subscribers.Count;
            int countToSkip = GetCountToSkip();
            int remainingCount = totalCount - countToSkip;
            int limit = _subscribers[countToSkip].Pinned ? 1 : Math.Min(remainingCount, MaxTiles);

            List<SharedClientScreen> newWindowClients = _subscribers.GetRange(countToSkip, limit);
            int numNewWindowClients = newWindowClients.Count;

            List<SharedClientScreen> previousWindowClients = this.CurrentWindowClients.ToList();
            previousWindowClients = previousWindowClients.Where(c => newWindowClients.FindIndex(n => n.Id == c.Id) == -1).ToList();

            var (newNumRows, newNumCols) = NumRowsColumns[numNewWindowClients];
            (int, int) newResolution = Resolution[numNewWindowClients];

            _ = this.ApplicationMainThreadDispatcher.BeginInvoke(
                        DispatcherPriority.Normal,
                        new Action<List<SharedClientScreen>, int, int, (int, int)>((newCurrentWindowClients, numNewRows, numNewCols, newRes) =>
                        {
                            lock (this)
                            {
                                // Note that Bitmap cannot be automatically marshaled to the main thread
                                // if it were created on the worker thread. Hence the data model just passes
                                // the path to the image, and the main thread creates an image from it.

                                this.CurrentWindowClients.Clear();
                                foreach (SharedClientScreen screen in newCurrentWindowClients)
                                {
                                    this.CurrentWindowClients.Add(screen);
                                }
                                this.CurrentPageRows = numNewRows;
                                this.CurrentPageColumns = numNewCols;
                                this.CurrentPageResolution = newRes;

                                this.OnPropertyChanged(nameof(this.CurrentWindowClients));
                                this.OnPropertyChanged(nameof(this.CurrentPageRows));
                                this.OnPropertyChanged(nameof(this.CurrentPageColumns));
                                this.OnPropertyChanged(nameof(this.CurrentPageResolution));
                            }
                        }),
                        newWindowClients, newNumRows, newNumCols, newResolution);

            NotifySubscribers(previousWindowClients, newWindowClients, newResolution);
        }

        // Mark the client as pinned. Switch to the page of that client
        // by setting the `CurrentPage` to new page of the pinned client
        public void OnPin(string clientId)
        {
            // mark as pinned
            SharedClientScreen pinnedScreen = _subscribers.Find(subs => subs.Id == clientId)!;
            pinnedScreen.Pinned = true;
            this.CurrentPage = GetClientPage(pinnedScreen.Id);
        }

        // Mark the client as unpinned
        // Switch to the max(first page, previous page)
        public void OnUnpin(string clientId)
        {
            SharedClientScreen unpinnedScreen = _subscribers.Find(subs => subs.Id == clientId)!;
            unpinnedScreen.Pinned = false;
            this.CurrentPage = Math.Max(1, this.CurrentPage - 1);
        }

        /// <summary>
        /// Handles the property changed event raised on a component.
        /// </summary>
        /// <param name="property">The name of the property.</param>
        private void OnPropertyChanged(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

        // Call Model for ->
        // Ask the previous clients to stop sending packets using `_model.BroadCastClients`
        // and call `StopProcessing` on them
        // Ask the new clients to start sending their packets using `_model.BroadCastClients`
        // and call `StartProcessing` on them with the below lambda function
        // The lambda function will take the image from the finalImageQueue
        // and set it as the `CurrentImage` variable
        private void NotifySubscribers(List<SharedClientScreen> prevWindowClients, List<SharedClientScreen> currentWindowClients, (int, int) resolution)
        {
            _model.BroadcastClients(currentWindowClients.Select(c => c.Id).ToList(), nameof(ServerDataHeader.Send), resolution);

            foreach (SharedClientScreen screen in currentWindowClients)
            {
                screen.StartProcessing(new Action<CancellationToken>((token) =>
                {
                    while (true)
                    {
                        // TODO: Use token logic
                        _ = this.ApplicationMainThreadDispatcher.BeginInvoke(
                        DispatcherPriority.Normal,
                        new Action<Bitmap>((image) =>
                        {
                            lock (screen)
                            {
                                // Note that Bitmap cannot be automatically marshaled to the main thread
                                // if it were created on the worker thread. Hence the data model just passes
                                // the path to the image, and the main thread creates an image from it.
                                screen.CurrentImage = image;

                                this.OnPropertyChanged(nameof(this.CurrentWindowClients));
                            }
                        }),
                        screen.GetFinalImage());
                    }
                }));
            }

            _model.BroadcastClients(prevWindowClients.Select(c => c.Id).ToList(), nameof(ServerDataHeader.Stop), Resolution[0]);

            foreach (SharedClientScreen screen in prevWindowClients)
            {
                screen.StopProcessing();
            }
        }

        private int GetCountToSkip()
        {
            int countToSkip = 0;
            for (int i = 0; i < _currentPage; ++i)
            {
                SharedClientScreen screen = _subscribers[countToSkip];
                if (screen.Pinned)
                {
                    ++countToSkip;
                }
                else
                {
                    countToSkip += MaxTiles;
                }
            }
            return countToSkip;
        }

        private int GetClientPage(string clientId)
        {
            int totalSubscribers = _subscribers.Count;
            int startSubsIdx = 0;
            int pageNum = 1;
            while (startSubsIdx < totalSubscribers)
            {
                SharedClientScreen screen = _subscribers[startSubsIdx];
                if (screen.Pinned)
                {
                    if (screen.Id == clientId) return pageNum;

                    ++startSubsIdx;
                }
                else
                {
                    int limit = Math.Min(MaxTiles, totalSubscribers - startSubsIdx);
                    int idx = _subscribers.GetRange(startSubsIdx, limit).FindIndex(sub => sub.Id == clientId);
                    if (idx >= 0) return pageNum;

                    startSubsIdx += MaxTiles;
                }
                ++pageNum;
            }
            return 1;
        }
    }
}
