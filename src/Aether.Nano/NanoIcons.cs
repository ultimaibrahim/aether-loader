using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Aether.Nano
{
    /// <summary>
    /// Cached mod icon loader shared by Aether's F10 Mod Menu, Aether Nano's standalone window,
    /// and any mod that wants to load its own embedded or sidecar PNG icon.
    /// Zero per-frame allocations: every lookup (hit or miss) is cached for the session.
    /// </summary>
    public static class NanoIcons
    {
        private const int MaxIconBytes = 2 * 1024 * 1024;
        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] DefaultResourceSuffixes =
        {
            ".icon.png",
            ".mod_icon.png",
            ".logo.png",
            "icon.png",
            "mod_icon.png",
            "logo.png"
        };

        /// <summary>
        /// Loads a PNG/JPG icon from an embedded manifest resource in <paramref name="assembly"/>.
        /// Pass <paramref name="resourceHint"/> (for example <c>"icon.png"</c>) or leave null to auto-detect
        /// <c>icon.png</c>, <c>mod_icon.png</c>, or <c>logo.png</c>.
        /// </summary>
        public static Texture2D LoadFromAssembly(Assembly assembly, string resourceHint = null)
        {
            if (assembly == null) return null;
            string key = "asm:" + (assembly.FullName ?? assembly.GetName().Name ?? "unknown") + "|" + (resourceHint ?? string.Empty);
            if (Cache.TryGetValue(key, out Texture2D cached)) return cached;

            Texture2D tex = null;
            try
            {
                string[] names = assembly.GetManifestResourceNames();
                string match = FindMatchingResource(names, resourceHint);
                if (!string.IsNullOrEmpty(match))
                {
                    using Stream s = assembly.GetManifestResourceStream(match);
                    if (s != null && s.Length > 0 && s.Length <= MaxIconBytes)
                    {
                        byte[] bytes = ReadAllBytes(s);
                        tex = DecodeTexture(bytes, match);
                    }
                }
            }
            catch { }

            Cache[key] = tex;
            return tex;
        }

        /// <summary>Loads a PNG/JPG icon from a file on disk, cached for the session.</summary>
        public static Texture2D LoadFromFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;
            string key = "file:" + filePath;
            if (Cache.TryGetValue(key, out Texture2D cached)) return cached;

            Texture2D tex = null;
            try
            {
                if (File.Exists(filePath))
                {
                    var fi = new FileInfo(filePath);
                    if (fi.Length > 0 && fi.Length <= MaxIconBytes)
                    {
                        byte[] bytes = File.ReadAllBytes(filePath);
                        tex = DecodeTexture(bytes, Path.GetFileName(filePath));
                    }
                }
            }
            catch { }

            Cache[key] = tex;
            return tex;
        }

        /// <summary>
        /// Resolves a mod icon automatically from:
        /// 1) <paramref name="explicitHint"/> as a file relative to the DLL folder or embedded resource,
        /// 2) an embedded <c>icon.png</c> / <c>mod_icon.png</c> / <c>logo.png</c> inside <paramref name="assembly"/>,
        /// 3) a sidecar <c>&lt;ModName&gt;.png</c> / <c>&lt;ModName&gt;.icon.png</c> next to the DLL (or <c>icon.png</c> inside a dedicated mod folder).
        /// </summary>
        public static Texture2D Resolve(Assembly assembly, string dllPath, string explicitHint = null)
        {
            string asmKey = assembly != null ? (assembly.FullName ?? assembly.GetName().Name ?? string.Empty) : string.Empty;
            string key = "res:" + asmKey + "|" + (dllPath ?? string.Empty) + "|" + (explicitHint ?? string.Empty);
            if (Cache.TryGetValue(key, out Texture2D cached)) return cached;

            Texture2D tex = null;
            string dir = !string.IsNullOrEmpty(dllPath) ? Path.GetDirectoryName(dllPath) : null;

            // 1. Explicit hint (file relative to DLL, or embedded resource)
            if (!string.IsNullOrWhiteSpace(explicitHint))
            {
                if (!string.IsNullOrEmpty(dir))
                {
                    string candidate = Path.IsPathRooted(explicitHint) ? explicitHint : Path.Combine(dir, explicitHint);
                    tex = LoadFromFile(candidate);
                }
                if (tex == null && assembly != null)
                    tex = LoadFromAssembly(assembly, explicitHint);
            }

            // 2. Auto-detect embedded icon.png inside the mod's DLL
            if (tex == null && assembly != null)
                tex = LoadFromAssembly(assembly, null);

            // 3. Auto-detect sidecar PNG next to the mod's DLL
            if (tex == null && !string.IsNullOrEmpty(dllPath) && !string.IsNullOrEmpty(dir))
            {
                string stem = Path.GetFileNameWithoutExtension(dllPath);
                if (!string.IsNullOrEmpty(stem))
                {
                    tex = LoadFromFile(Path.Combine(dir, stem + ".png"))
                          ?? LoadFromFile(Path.Combine(dir, stem + ".icon.png"));
                }

                // Only check bare icon.png / assets/icon.png if the mod sits in its own dedicated folder
                if (tex == null && !IsSharedModsRoot(dir))
                {
                    tex = LoadFromFile(Path.Combine(dir, "icon.png"))
                          ?? LoadFromFile(Path.Combine(dir, "assets", "icon.png"))
                          ?? LoadFromFile(Path.Combine(dir, "textures", "icon.png"));
                }
            }

            Cache[key] = tex;
            return tex;
        }

        /// <summary>Draws a mod icon fitted inside <paramref name="rect"/> with clean rounded corners.</summary>
        public static void Draw(Rect rect, Texture2D icon, float radius = 6f)
        {
            if (icon == null || rect.width < 2f || rect.height < 2f) return;

            float iw = Mathf.Max(1f, icon.width);
            float ih = Mathf.Max(1f, icon.height);
            float scale = Mathf.Min(rect.width / iw, rect.height / ih);
            float dw = iw * scale;
            float dh = ih * scale;
            var drawRect = new Rect(
                rect.x + (rect.width - dw) * 0.5f,
                rect.y + (rect.height - dh) * 0.5f,
                dw,
                dh);

            float r = Mathf.Clamp(radius, 0f, Mathf.Min(dw, dh) * 0.5f);
            if (r > 0.5f)
            {
                GUI.DrawTexture(drawRect, icon, ScaleMode.StretchToFill, true, 0f, Color.white,
                    Vector4.zero, new Vector4(r, r, r, r));
            }
            else
            {
                GUI.DrawTexture(drawRect, icon, ScaleMode.ScaleToFit, true);
            }
        }

        private static bool IsSharedModsRoot(string dir)
        {
            string name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.Equals(name, "Mods", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "plugins", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(name, "Aether", StringComparison.OrdinalIgnoreCase);
        }

        private static string FindMatchingResource(string[] names, string hint)
        {
            if (names == null || names.Length == 0) return null;

            if (!string.IsNullOrWhiteSpace(hint))
            {
                string norm = hint.Trim().Replace('\\', '.').Replace('/', '.');
                for (int i = 0; i < names.Length; i++)
                {
                    if (string.Equals(names[i], norm, StringComparison.OrdinalIgnoreCase) ||
                        names[i].EndsWith("." + norm, StringComparison.OrdinalIgnoreCase) ||
                        names[i].EndsWith(norm, StringComparison.OrdinalIgnoreCase))
                    {
                        return names[i];
                    }
                }
            }

            for (int s = 0; s < DefaultResourceSuffixes.Length; s++)
            {
                string suffix = DefaultResourceSuffixes[s];
                for (int i = 0; i < names.Length; i++)
                {
                    if (names[i].EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        return names[i];
                }
            }
            return null;
        }

        private static byte[] ReadAllBytes(Stream s)
        {
            var bytes = new byte[s.Length];
            int read = 0;
            while (read < bytes.Length)
            {
                int n = s.Read(bytes, read, bytes.Length - read);
                if (n <= 0) break;
                read += n;
            }
            return bytes;
        }

        private static Texture2D DecodeTexture(byte[] bytes, string label)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                name = "NanoIcon_" + label,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };
            if (!tex.LoadImage(bytes))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            return tex;
        }
    }
}
