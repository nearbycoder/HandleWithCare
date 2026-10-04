using UnityEngine;

namespace HWC
{
    /// <summary>
    /// Unscaled time that follows Time.captureDeltaTime when it is set (trailer capture), so menus,
    /// journey playback and camera smoothing advance one fixed step per captured frame. In normal play
    /// it is exactly Time.unscaledDeltaTime / Time.unscaledTime.
    /// </summary>
    public static class Clock
    {
        public static float UnscaledDelta => Time.captureDeltaTime > 0f ? Time.captureDeltaTime : Time.unscaledDeltaTime;
        public static float UnscaledTime => Time.captureDeltaTime > 0f ? Time.frameCount * Time.captureDeltaTime : Time.unscaledTime;
    }
}
