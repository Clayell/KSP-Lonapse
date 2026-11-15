// A short note the first time a save opens at the Space Center: where the settings are, that the voice runs on this
// computer and how to mute it. Shown once per install (Settings.welcomeShown), also after an update from a version
// without the note. The flight addon loads the settings again when a flight starts, so it sees the new value.
using System;
using UnityEngine;

namespace Lonapse
{
    [KSPAddon(KSPAddon.Startup.SpaceCentre, true)]
    public sealed class WelcomeNote : MonoBehaviour
    {
        /// <summary>Seconds after the Space Center loaded: the scene and its own dialogs come first.</summary>
        const float ShowDelay = 2f;

        private void Start()
        {
            try { Invoke("Show", ShowDelay); }
            catch (Exception e) { Log.Exception("WelcomeNote.Start", e); }
        }

        private void Show()
        {
            try
            {
                Settings s = Settings.Load();
                if (s.loadFailed || s.welcomeShown) return;
                s.welcomeShown = true;
                s.Save();
                PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    new MultiOptionDialog("LonapseWelcome", Text(s), "Lonapse " + LonapseAddon.Version, HighLogic.UISkin,
                        new DialogGUIButton("Got it", () => { }, true)),
                    false, HighLogic.UISkin);
                Log.Info("Welcome note shown");
            }
            catch (Exception e) { Log.Exception("WelcomeNote.Show", e); }
        }

        static string Text(Settings s)
        {
            string mute = (s.MuteModifierCode == KeyCode.None ? "" : s.MuteModifierCode + "+") + s.MuteKeyCode;
            return "Your crew, Mission Control and your probes now talk on the radio, with subtitles.\n\n"
                 + "The settings are behind the Lonapse button in the toolbar during a flight. "
                 + "In the VAB and SPH the Lonapse button sets up voicelines for action groups.\n\n"
                 + "The voices are made on this computer, nothing goes online. "
                 + "The first lines of a flight can take a moment while the voices load.\n\n"
                 + mute + " mutes or unmutes Lonapse during a flight.\n\n"
                 + "Something wrong? Lines starting with [Lonapse] in KSP.log help a lot in a bug report.";
        }
    }
}
