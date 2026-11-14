namespace Aether.Core.Entities
{
    /// <summary>
    /// Rules for deciding whether a raycast hit counts as "pointing at an entity". Mod-made mobs and pets
    /// often use trigger colliders as their hitbox, so triggers must be accepted, but not the large trigger volumes
    /// that mobs also carry (aggro / detection ranges), which would make a mob "visible" from far away.
    /// </summary>
    public static class EntityHitRules
    {
        /// <summary>Largest trigger (half-diagonal of its bounds, in blocks) still considered a body hitbox.</summary>
        public const float MaxTriggerExtent = 4.5f;

        public static bool AcceptsHit(bool colliderIsTrigger, float boundsExtentMagnitude) =>
            !colliderIsTrigger || (boundsExtentMagnitude > 0f && boundsExtentMagnitude <= MaxTriggerExtent);
    }
}
