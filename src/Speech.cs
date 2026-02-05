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
        private static bool _initialized;
        private static bool _prioritySpeaking;

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

        /// <summary>
        /// Strip color markup, sanitize decorative characters, return clean text.
        /// </summary>
        internal static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            string clean = ConsoleLib.Console.ColorUtility.StripFormatting(text);
            if (string.IsNullOrEmpty(clean))
                return null;

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
        /// Used for high-priority announcements (popups, screen titles).
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
            _prioritySpeaking = true;
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
        /// Only speak if the text differs from the last spoken text.
        /// Will not interrupt priority speech (from Interrupt()) that is
        /// still playing — prevents navigation from cutting off popups.
        /// </summary>
        public static void SayIfNew(string text)
        {
            string clean = Clean(text);
            if (clean == null)
                return;

            if (clean == _lastSpoken)
                return;

            EnsureInitialized();

            // If priority speech (popups, screen titles) is still playing,
            // queue behind it instead of interrupting.
            if (!(_prioritySpeaking && WindowsTTS.IsSpeaking()))
                WindowsTTS.Stop();

            _prioritySpeaking = false;
            WindowsTTS.Speak(clean);
            _lastSpoken = clean;
        }
    }
}
