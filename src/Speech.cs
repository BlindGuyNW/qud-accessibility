using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace QudAccessibility
{
    /// <summary>
    /// Thin static wrapper around the game's existing WindowsTTS to provide
    /// convenience methods for screen reader vocalization.
    /// Lazily ensures the WindowsTTS MonoBehaviour is created and initialized.
    /// </summary>
    public static class Speech
    {
        private static string _lastSpoken;
        private static string _lastNavSpoken;
        private static bool _initialized;
        private static float _priorityUntil;

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            // Create the WindowsTTS MonoBehaviour if it doesn't exist yet.
            // This mirrors what UAP_AudioQueue.InitializeWindowsTTS() does.
            if (WindowsTTS.instance == null)
            {
                var go = new GameObject("QudAccessibility_WindowsTTS");
                go.AddComponent<WindowsTTS>();
                Object.DontDestroyOnLoad(go);
            }

            ScreenReader.EnsureInitialized();
            _initialized = true;
        }

        /// <summary>
        /// Remove decorative Unicode characters that screen readers vocalize
        /// as their Unicode names (e.g. ù read as "u-grave").
        /// </summary>
        private static string Sanitize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return text;

            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                // Replace ù (U+00F9) bullet points with dash
                if (c == '\u00f9')
                {
                    sb.Append('-');
                    continue;
                }

                // Replace CP437 arrow control characters and Unicode arrow symbols
                // with readable text. The game sets arrow key display names to
                // CP437 control chars (\u0018-\u001b) which are silent to screen readers.
                if (c == '\u0018' || c == '\u2191') { sb.Append("Up Arrow"); continue; }
                if (c == '\u0019' || c == '\u2193') { sb.Append("Down Arrow"); continue; }
                if (c == '\u001a' || c == '\u2192') { sb.Append("Right Arrow"); continue; }
                if (c == '\u001b' || c == '\u2190') { sb.Append("Left Arrow"); continue; }

                // Strip box-drawing characters (U+2500–U+257F)
                if (c >= '\u2500' && c <= '\u257F')
                    continue;

                // Strip block elements (U+2580–U+259F)
                if (c >= '\u2580' && c <= '\u259F')
                    continue;

                // Strip geometric shapes (U+25A0–U+25FF)
                if (c >= '\u25A0' && c <= '\u25FF')
                    continue;

                sb.Append(c);
            }

            return sb.ToString().Trim();
        }

        // Cached command keys sorted by length (longest first) for ~/% substitution
        private static List<string> _commandKeys;

        /// <summary>
        /// Replace ~CmdFoo and %CmdFoo markers with actual key binding names.
        /// The game uses ~ for primary binding and % for all bindings;
        /// for TTS we resolve both the same way.
        /// </summary>
        private static string ResolveCommands(string text)
        {
            if (text == null || (!text.Contains("~") && !text.Contains("%")))
                return text;

            // Handle ~Highlight specially (Alt key indicator)
            if (text.Contains("~Highlight"))
            {
                if (ControlManager.activeControllerType != ControlManager.InputDeviceType.Gamepad)
                    text = text.Replace("~Highlight", "Alt");
                else
                    text = text.Replace("~Highlight", "");
            }

            if (!text.Contains("~") && !text.Contains("%"))
                return text;

            // Build sorted key list on first use
            if (_commandKeys == null && XRL.UI.CommandBindingManager.CommandBindings != null)
            {
                _commandKeys = XRL.UI.CommandBindingManager.CommandBindings.Keys.ToList();
                _commandKeys.Sort((a, b) => b.Length - a.Length);
            }

            if (_commandKeys == null)
                return text;

            for (int i = 0; i < _commandKeys.Count; i++)
            {
                string key = _commandKeys[i];
                string tildeMarker = "~" + key;
                string pctMarker = "%" + key;
                if (text.Contains(tildeMarker))
                {
                    string binding = ControlManager.getCommandInputDescription(key, mapGlyphs: false);
                    text = text.Replace(tildeMarker, binding ?? key);
                }
                if (text.Contains(pctMarker))
                {
                    string binding = ControlManager.getCommandInputDescription(key, mapGlyphs: false);
                    text = text.Replace(pctMarker, binding ?? key);
                }
                if (!text.Contains("~") && !text.Contains("%"))
                    break;
            }

            return text;
        }

        /// <summary>
        /// Strip color markup, resolve command bindings, sanitize decorative
        /// characters, return clean text.
        /// </summary>
        internal static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            string clean = ConsoleLib.Console.ColorUtility.StripFormatting(text);
            if (string.IsNullOrEmpty(clean))
                return null;

            clean = ResolveCommands(clean);

            clean = Sanitize(clean);
            if (string.IsNullOrEmpty(clean))
                return null;

            return clean;
        }

        /// <summary>
        /// Map a Qud color char to a human-readable color name.
        /// Returns null for default gray (y), black (k/K), and unknown chars.
        /// </summary>
        internal static string GetColorName(char colorChar)
        {
            switch (colorChar)
            {
                case 'Y': return "white";
                case 'W': return "gold";
                case 'w': return "brown";
                case 'G': return "green";
                case 'g': return "dark green";
                case 'C': return "cyan";
                case 'c': return "dark cyan";
                case 'R': return "red";
                case 'r': return "dark red";
                case 'M': return "magenta";
                case 'm': return "dark magenta";
                case 'B': return "blue";
                case 'b': return "dark blue";
                case 'O': return "orange";
                case 'o': return "dark orange";
                default: return null;
            }
        }

        /// <summary>
        /// Get a color suffix string for a game object, e.g. " (gold)" or "".
        /// Skips default gray and null objects. Uses DisplayNameColor for accuracy.
        /// Must fully qualify XRL.World.GameObject to avoid UnityEngine ambiguity.
        /// </summary>
        internal static string GetObjectColorSuffix(XRL.World.GameObject go)
        {
            if (go == null)
                return "";
            string colorStr = go.DisplayNameColor;
            if (string.IsNullOrEmpty(colorStr))
                return "";
            string name = GetColorName(colorStr[0]);
            return name != null ? " (" + name + ")" : "";
        }

        /// <summary>
        /// Reset the SayIfNew dedup tracker. Call on screen transitions
        /// so that the first scroller item is announced even if it matches
        /// the previous screen's last navigation speech.
        /// </summary>
        public static void ResetNavigation()
        {
            _lastNavSpoken = null;
        }

        /// <summary>
        /// Strip color markup and speak the text.
        /// </summary>
        public static void Say(string text)
        {
            string clean = Clean(text);
            if (clean == null)
                return;

            EnsureInitialized();
            WindowsTTS.Speak(clean);
            _lastSpoken = clean;
        }

        /// <summary>
        /// Stop any current speech, then speak new text.
        /// Used for user-initiated actions (F2 re-read, F3/F4 blocks, scanner,
        /// attribute changes) where only the latest utterance matters.
        /// Navigation speech (SayIfNew) will not interrupt until this finishes.
        /// </summary>
        public static void Interrupt(string text)
        {
            string clean = Clean(text);
            if (clean == null)
                return;

            EnsureInitialized();
            WindowsTTS.Stop();
            WindowsTTS.Speak(clean);
            _lastSpoken = clean;
            _lastNavSpoken = null;
            _priorityUntil = Time.unscaledTime + clean.Length / 10f;
        }

        /// <summary>
        /// Speak text without cancelling current speech. Used for system-initiated
        /// announcements (popups, screen titles) that should queue behind any
        /// prior speech rather than cancelling it.
        /// Updates _lastSpoken but not _lastNavSpoken — SayIfNew has its own
        /// dedup tracker so screen titles don't cause scroller re-announcements.
        /// Navigation speech (SayIfNew) will not interrupt until this finishes.
        /// </summary>
        public static void Announce(string text)
        {
            string clean = Clean(text);
            if (clean == null)
                return;

            EnsureInitialized();
            WindowsTTS.Speak(clean);
            _lastSpoken = clean;
            _priorityUntil = Time.unscaledTime + clean.Length / 10f;
        }

        /// <summary>
        /// Speak text without stopping current speech. Used for message log
        /// entries that should queue behind whatever is currently playing.
        /// </summary>
        public static void Queue(string text)
        {
            string clean = Clean(text);
            if (clean == null)
                return;

            EnsureInitialized();
            WindowsTTS.Speak(clean);
        }

        /// <summary>
        /// Only speak if the text differs from the last navigation speech.
        /// Uses a separate dedup tracker (_lastNavSpoken) so that Announce()
        /// calls (screen titles) don't reset dedup and cause re-announcement
        /// of the same scroller item. Also checks _lastSpoken to avoid
        /// repeating text that was just spoken by Announce/Interrupt.
        /// Will not interrupt priority speech that is still playing.
        /// </summary>
        public static void SayIfNew(string text)
        {
            string clean = Clean(text);
            if (clean == null)
                return;

            if (clean == _lastNavSpoken || clean == _lastSpoken)
                return;

            EnsureInitialized();

            // If priority speech is still playing (estimated by text length),
            // queue behind it instead of interrupting.
            if (Time.unscaledTime < _priorityUntil)
                WindowsTTS.Speak(clean);
            else
            {
                WindowsTTS.Stop();
                WindowsTTS.Speak(clean);
            }
            _lastNavSpoken = clean;
            _lastSpoken = clean;
        }
    }
}
