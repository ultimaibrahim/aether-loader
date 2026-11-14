using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AetherNano
{
    /// <summary>
    /// A small "Modded" mark on the main menu, drawn only when Aether is not loaded (Aether has its own mod menu).
    /// Every mod embeds its own copy of this code, so the copies agree through a well-known GameObject: the first one
    /// creates it, the others hand it their name with SendMessage (which works across assemblies). One mark, however many mods.
    /// </summary>
    internal static class NanoBadge
    {
        internal const string ObjectName = "AetherNanoBadge";

        internal static void Register(string modName, bool show)
        {
            GameObject host = GameObject.Find(ObjectName);
            if (host == null)
            {
                host = new GameObject(ObjectName);
                Object.DontDestroyOnLoad(host);
                host.AddComponent<NanoBadgeBehaviour>();
            }
            host.SendMessage("NanoRegister", NanoBadgeText.Encode(modName, show), SendMessageOptions.DontRequireReceiver);
        }
    }

    internal sealed class NanoBadgeBehaviour : MonoBehaviour
    {
        private readonly List<string> _names = new List<string>();
        private bool _anyWantsIt;
        private string _text = string.Empty;
        private string _details = string.Empty;
        private string _detailsRich = string.Empty;
        private string _aetherLine = string.Empty;
        private GUIStyle _style;
        private GUIStyle _aetherStyle;
        private GUIStyle _detailStyle;

        private void Awake()
        {
            string bepInEx = string.Empty;
            try { bepInEx = BepInExVersion(); } catch { }
            _details = NanoBadgeText.BuildDetails(bepInEx, Application.version, Application.unityVersion);
            _detailsRich = NanoBadgeText.BuildDetails(bepInEx, Application.version, Application.unityVersion, rich: true);
            _aetherLine = NanoBadgeText.BuildAetherLine(AetherLink.IsPresent);
        }

        private static string BepInExVersion()
        {
            System.Reflection.Assembly asm = typeof(BepInEx.Paths).Assembly;
            object[] info = asm.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
            string text = info.Length > 0 ? ((System.Reflection.AssemblyInformationalVersionAttribute)info[0]).InformationalVersion : asm.GetName().Version.ToString();
            int plus = text.IndexOf('+'); // drop a build-metadata suffix such as "+abc123"
            return plus > 0 ? text.Substring(0, plus) : text;
        }

        // Called through SendMessage by every Nano mod, including copies compiled into other assemblies.
        private void NanoRegister(string payload)
        {
            if (!NanoBadgeText.TryDecode(payload, out string name, out bool show)) return;
            if (!_names.Contains(name)) _names.Add(name);
            _anyWantsIt |= show;
            _text = NanoBadgeText.Build(_names);
        }

        private void OnGUI()
        {
            if (!_anyWantsIt || _text.Length == 0) return;
            if (!NanoBadgeText.IsMainMenuScene(SceneManager.GetActiveScene().name)) return;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { fontSize = 14, alignment = TextAnchor.LowerLeft };
                _style.normal.textColor = new Color(1f, 1f, 1f, 0.75f);
                // Aether has its own line: amber while it is missing, green once it is there.
                _aetherStyle = new GUIStyle(_style) { fontSize = 12 };
                _aetherStyle.normal.textColor = AetherLink.IsPresent ? new Color(0.55f, 0.95f, 0.55f, 0.9f) : new Color(1f, 0.78f, 0.35f, 0.9f);
                _detailStyle = new GUIStyle(_style) { fontSize = 12, richText = true };
                _detailStyle.normal.textColor = new Color(1f, 1f, 1f, 0.6f);
            }

            int oldDepth = GUI.depth;
            GUI.depth = -1000;
            // Three lines at the bottom-left: the mods, Aether's status, then BepInEx / game / Unity. A soft shadow keeps
            // them readable on any menu background.
            float width = Screen.width - 20f;
            DrawLine(new Rect(10f, Screen.height - 76f, width, 26f), _text, _text, _style);
            if (_aetherLine.Length > 0) DrawLine(new Rect(10f, Screen.height - 54f, width, 22f), _aetherLine, _aetherLine, _aetherStyle);
            if (_details.Length > 0) DrawLine(new Rect(10f, Screen.height - 32f, width, 22f), _detailsRich, _details, _detailStyle);
            GUI.depth = oldDepth;
        }

        private static void DrawLine(Rect rect, string text, string plain, GUIStyle style)
        {
            GUIStyle shadow = new GUIStyle(style) { richText = false };
            shadow.normal.textColor = new Color(0f, 0f, 0f, 0.6f);
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), plain, shadow);
            GUI.Label(rect, text, style);
        }
    }
}
