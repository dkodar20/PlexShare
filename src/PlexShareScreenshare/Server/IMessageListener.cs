using System.Collections.Generic;

namespace PlexShareScreenshare.Server
{
    /// <summary>
    /// Notifies clients that has a message has been received.
    /// </summary>
    public interface IMessageListener
    {
        public void OnSubscribersChanged(List<SharedClientScreen> sharedClientScreens);
    }
}
