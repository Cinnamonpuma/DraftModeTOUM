using TownOfUs.Assets;
using TownOfUs.Utilities;
using UnityEngine;

namespace DraftModeTOUM
{
    internal static class DraftAudio
    {
        /// <summary>
        /// Plays a single gentle cue when draft mode starts so tabbed-out players
        /// still get an audible signal that role picking has begun.
        /// </summary>
        public static void PlayDraftStartCue()
        {
            try
            {
                if (!Constants.ShouldPlaySfx() || SoundManager.Instance == null)
                    return;

                // QuestionSound reads more like a clean chime, with TrackerActivate as fallback.
                var clip = TouAudio.QuestionSound.LoadAsset();
                clip ??= TouAudio.TrackerActivateSound.LoadAsset();
                if (clip == null)
                    return;

                var source = SoundManager.Instance.PlaySound(clip, false, 1.05f);
                if (source != null)
                {
                    source.pitch = 1.02f;
                }
            }
            catch (System.Exception ex)
            {
                DraftModePlugin.Logger?.LogWarning($"[DraftAudio] Failed to play draft-start cue: {ex.Message}");
            }
        }
    }
}
