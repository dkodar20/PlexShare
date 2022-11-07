using PlexShareNetwork;
using PlexShareNetwork.Communication;
using PlexShareNetwork.Serialization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Timers;
// Each frame consists of the resolution of the image and the ImageDiffList
using Frame = System.Tuple<System.Tuple<int, int>,
                        System.Collections.Generic.List<System.Tuple<System.Tuple<int, int>,
                        System.Tuple<int, int, int>>>>;

namespace PlexShareScreenshare.Server
{
    // Provided by Networking module
    public class ScreenShareServer : INotificationHandler, ITimerManager
    {
        /// <summary>
        /// The string representing the module identifier for screen share
        /// </summary>
        public static readonly string ModuleIdentifier = "ScreenShare";

        private static ScreenShareServer? _instance;

        // The Networking object to send packets and subscribe to it
        // Initialized in the constructor
        // Will have `SendMessage` and `Subscribe`
        private readonly ICommunicator _communicator;

        // Stores an instance of the _serializer
        private readonly ISerializer _serializer;

        // The subscriber.
        private readonly IMessageListener _client;

        // The map between each ID and the shared screen object for
        // all the active subscribers (screen sharers)
        private readonly Dictionary<string, SharedClientScreen> _subscribers;

        // The constructor for this class. It will instantiate the
        // Subscribe the networking by calling `_communicator.Subscribe()`
        protected ScreenShareServer(IMessageListener listener)
        {
            _communicator = CommunicationFactory.GetCommunicator(isClient: false);
            _communicator.Subscribe(ModuleIdentifier, this);
            _subscribers = new Dictionary<string, SharedClientScreen>();
            _serializer = new Serializer();
            _client = listener;
        }

        // This method will be invoked by the networking team
        // This will be the response packets from the clients
        // Based on the header in the packet received, do further
        // processing as follows:
        /*
            REGISTER     --> RegisterClient(ip)
            DEREGISTER   --> DeregisterClient(ip)
            IMAGE        --> ENQUEUE(ip_shared_screen, image)
            CONFIRMATION --> UpdateTimer(ip)
        */
        public void OnDataReceived(string serializedData)
        {
            DataPacket packet = _serializer.Deserialize<DataPacket>(serializedData);
            string clientId = packet.Id;
            string clientName = packet.Name;
            ClientDataHeader header = Enum.Parse<ClientDataHeader>(packet.Header);
            string clientData = packet.Data;

            switch (header)
            {
                case ClientDataHeader.Register:
                    RegisterClient(clientId, clientName);
                    break;
                case ClientDataHeader.Deregister:
                    DeregisterClient(clientId);
                    break;
                case ClientDataHeader.Image:
                    PutImage(clientId, clientData);
                    break;
                case ClientDataHeader.Confirmation:
                    UpdateTimer(clientId);
                    break;
            }
        }

        /// <summary>
        /// Called by the Communicator when a module declares that it wants the networking module for communication
        /// It maps the socket object to the module which calls this function
        /// </summary>
        public void OnClientJoined<T>(T socket) { }

        /// <summary>
        /// Called by the Communicator when a module declares that it no more needs the networking module
        /// The mapping of a socket object to the module is erased
        /// </summary>
        public void OnClientLeft(string clientId) { }

        public static ScreenShareServer GetInstance(IMessageListener listener)
        {
            _instance ??= new ScreenShareServer(listener);
            return _instance;
        }

        // Tell the clients the information about the resolution
        // of the image to be sent and whether to send the image or not
        public void BroadcastClients(List<string> clientIds, string header, (int, int) resolution)
        {
            string serializedData = _serializer.Serialize(resolution);
            DataPacket packet = new("1", "Server", header, serializedData);
            string serializedPacket = _serializer.Serialize(packet);

            foreach (string clientId in clientIds)
            {
                _communicator.Send(serializedPacket, ModuleIdentifier, clientId);
            }
        }

        // Callback method for the timer
        // It will De-register the client
        public void OnTimeOut(object? source, ElapsedEventArgs e, string clientId)
        {
            DeregisterClient(clientId);
        }

        // Add this client to the map after calling the constructor
        // Notify the view model to recompute current window clients
        // View model will call the `StartProcessing` for current window clients
        private void RegisterClient(string clientId, string clientName)
        {
            _subscribers.Add(clientId, new SharedClientScreen(clientId, clientName, this));
            NotifyClient();
        }

        // Remove this client from the map
        // Calls `StopProcessing` for the client
        // Notify the view model to recompute current window clients
        private void DeregisterClient(string clientId)
        {
            _subscribers.TryGetValue(clientId, out SharedClientScreen? sharedClientScreen);
            _ = _subscribers.Remove(clientId);
            sharedClientScreen.StopProcessing();
            NotifyClient();
        }

        // Calls the `SharedClientScreen.PutImage(image)`
        private void PutImage(string clientId, string data)
        {
            Frame frame = _serializer.Deserialize<Frame>(data);
            _ = _subscribers.TryGetValue(clientId, out SharedClientScreen? sharedClientScreen);
            sharedClientScreen.PutImage(frame);
        }

        // Reset the timer for the client with the `OnTimeOut()`
        private void UpdateTimer(string clientId)
        {
            _ = _subscribers.TryGetValue(clientId, out SharedClientScreen? sharedClientScreen);
            sharedClientScreen.UpdateTimer();
        }

        private void NotifyClient()
        {
            _client.OnSubscribersChanged(_subscribers.Values.ToList());
        }
    }
}
