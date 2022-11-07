/// <author>Mayank Singla</author>
/// <summary>
/// Summary Here...
/// </summary>

using System.Timers;

namespace PlexShareScreenshare.Server
{
    public interface ITimerManager
    {
        public void OnTimeOut(object? source, ElapsedEventArgs e, string id);
    }
}
