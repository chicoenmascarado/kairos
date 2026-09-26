using Windows.Media.Control;

namespace KairosDock.Services;

/// <summary>
/// Reads the system "now playing" media (title / artist / play state) from the
/// Windows SMTC, so the Control Center can show what's playing. Playback control
/// itself is done with the global media keys (see <see cref="SystemStatus"/>),
/// which is the most universally-compatible way.
/// </summary>
internal static class MediaControl
{
    public readonly record struct NowPlaying(bool HasSession, string Title, string Artist, bool Playing);

    public static async Task<NowPlaying> GetAsync()
    {
        try
        {
            var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var session = mgr.GetCurrentSession();
            if (session == null)
                return new NowPlaying(false, "", "", false);

            bool playing = session.GetPlaybackInfo().PlaybackStatus
                == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            string title = "", artist = "";
            try
            {
                var m = await session.TryGetMediaPropertiesAsync();
                title = m.Title ?? "";
                artist = m.Artist ?? "";
            }
            catch { /* some sources don't expose properties */ }

            return new NowPlaying(true, title, artist, playing);
        }
        catch { return new NowPlaying(false, "", "", false); }
    }
}
