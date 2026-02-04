using System.Collections.Generic;
using UnityEngine;

namespace QudAccessibility
{
    /// <summary>
    /// MonoBehaviour that provides keyboard shortcuts for accessibility:
    ///   F2 = re-read full screen content
    ///   F3 = next content block
    ///   F4 = previous content block
    /// Tracks current screen content and navigable blocks.
    /// Created lazily by Speech.EnsureInitialized().
    /// </summary>
    public class ScreenReader : MonoBehaviour
    {
        private static ScreenReader _instance;

        // Full screen content for F2 re-read
        private static string _screenContent;

        // Navigable content blocks for F3/F4
        private static readonly List<ContentBlock> _blocks = new List<ContentBlock>();
        private static int _blockIndex = -1;

        public struct ContentBlock
        {
            public string Title;
            public string Body;

            public string ToSpeech()
            {
                if (!string.IsNullOrEmpty(Title) && !string.IsNullOrEmpty(Body))
                    return Title + ": " + Body;
                return Title ?? Body ?? "";
            }
        }

        /// <summary>
        /// Store the current screen's readable content for F2 re-read.
        /// Also clears any previous block navigation.
        /// </summary>
        public static void SetScreenContent(string content)
        {
            _screenContent = content;
            _blocks.Clear();
            _blockIndex = -1;
        }

        /// <summary>
        /// Set navigable content blocks for F3/F4 cycling.
        /// Does not affect the F2 screen content.
        /// </summary>
        public static void SetBlocks(List<ContentBlock> blocks)
        {
            _blocks.Clear();
            if (blocks != null)
                _blocks.AddRange(blocks);
            _blockIndex = -1;
        }

        public static void EnsureInitialized()
        {
            if (_instance != null)
                return;

            var go = new GameObject("QudAccessibility_ScreenReader");
            _instance = go.AddComponent<ScreenReader>();
            Object.DontDestroyOnLoad(go);
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
            {
                if (!string.IsNullOrEmpty(_screenContent))
                {
                    Speech.Interrupt(_screenContent);
                }
            }

            if (Input.GetKeyDown(KeyCode.F3))
            {
                NavigateBlock(1);
            }

            if (Input.GetKeyDown(KeyCode.F4))
            {
                NavigateBlock(-1);
            }
        }

        private static void NavigateBlock(int direction)
        {
            if (_blocks.Count == 0)
                return;

            _blockIndex += direction;

            // Wrap around
            if (_blockIndex >= _blocks.Count)
                _blockIndex = 0;
            if (_blockIndex < 0)
                _blockIndex = _blocks.Count - 1;

            string text = _blocks[_blockIndex].ToSpeech();
            if (!string.IsNullOrEmpty(text))
            {
                Speech.Interrupt(text);
            }
        }
    }
}
