using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Aether.Registry
{
    public sealed class BlockDefinition
    {
        public Identifier Id { get; }
        public string DisplayName { get; set; }
        public float Hardness { get; set; } = 1.0f;
        public bool IsSolid { get; set; } = true;
        public bool IsTransparent { get; set; } = false;
        public string? TexturePath { get; set; }

        public BlockDefinition(Identifier id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
        }
    }

    /// <summary>
    /// Dynamic, namespaced block registry with world ID persistence.
    /// Completely eliminates raw numeric ID conflicts and guarantees save-game immutability.
    /// </summary>
    public sealed class DynamicBlockRegistry
    {
        public static DynamicBlockRegistry Instance { get; } = new();

        public const ushort CustomBlockIdStart = 500;

        private readonly Dictionary<Identifier, ushort> _idByIdentifier = new();
        private readonly Dictionary<ushort, Identifier> _identifierById = new();
        private readonly Dictionary<Identifier, BlockDefinition> _definitions = new();

        public ushort Register(BlockDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            if (_idByIdentifier.TryGetValue(definition.Id, out ushort existingId))
            {
                _definitions[definition.Id] = definition;
                return existingId;
            }

            ushort allocatedId = AllocateStableId(definition.Id);
            _idByIdentifier[definition.Id] = allocatedId;
            _identifierById[allocatedId] = definition.Id;
            _definitions[definition.Id] = definition;

            return allocatedId;
        }

        /// <summary>
        /// Derives an id deterministically from the identifier (FNV-1a) with linear probing.
        /// Sequential allocation meant that adding or removing a mod shifted every custom block id,
        /// so an existing world would resolve old ids to the wrong blocks.
        /// </summary>
        private ushort AllocateStableId(Identifier id)
        {
            const uint FnvOffset = 2166136261u;
            const uint FnvPrime = 16777619u;

            string text = id.ToString();
            uint hash = FnvOffset;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= char.ToLowerInvariant(text[i]);
                hash *= FnvPrime;
            }

            int range = ushort.MaxValue - CustomBlockIdStart + 1;
            int start = (int)(hash % (uint)range);

            for (int probe = 0; probe < range; probe++)
            {
                ushort candidate = (ushort)(CustomBlockIdStart + ((start + probe) % range));
                if (!_identifierById.ContainsKey(candidate))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException("Aether custom block id space is exhausted.");
        }

        public bool TryGetId(Identifier id, out ushort blockId) => _idByIdentifier.TryGetValue(id, out blockId);

        public bool TryGetIdentifier(ushort blockId, out Identifier id) => _identifierById.TryGetValue(blockId, out id);

        public bool TryGetDefinition(Identifier id, out BlockDefinition definition) =>
            _definitions.TryGetValue(id, out definition!);

        public void SaveWorldMapping(string worldDir)
        {
            if (string.IsNullOrWhiteSpace(worldDir)) return;
            Directory.CreateDirectory(worldDir);

            string mapFile = Path.Combine(worldDir, "aether_id_map.json");
            var map = new Dictionary<string, int>();

            foreach (var (id, numId) in _idByIdentifier)
            {
                map[id.ToString()] = numId;
            }

            string json = JsonConvert.SerializeObject(map, Formatting.Indented);
            File.WriteAllText(mapFile, json);
        }

        public void LoadWorldMapping(string worldDir)
        {
            if (string.IsNullOrWhiteSpace(worldDir)) return;
            string mapFile = Path.Combine(worldDir, "aether_id_map.json");
            if (!File.Exists(mapFile)) return;

            string json = File.ReadAllText(mapFile);
            var map = JsonConvert.DeserializeObject<Dictionary<string, int>>(json);
            if (map == null) return;

            foreach (var (rawId, numId) in map)
            {
                if (numId < CustomBlockIdStart || numId > ushort.MaxValue) continue;
                if (!Identifier.TryParse(rawId, out var id)) continue;

                ushort uNumId = (ushort)numId;

                // Never let a corrupt/duplicated entry steal an id already owned by another block.
                if (_identifierById.TryGetValue(uNumId, out Identifier existing) && !existing.Equals(id))
                {
                    continue;
                }

                _idByIdentifier[id] = uNumId;
                _identifierById[uNumId] = id;
            }
        }
    }
}
