using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace betterhusbandry
{
    /// <summary>
    /// Config for a single modifier axis (feed or interact). All values are
    /// player-editable via VintagestoryData/ModConfig/betterhusbandry.json and
    /// reloaded live - see betterhusbandryModSystem.OnConfigReloadTick.
    /// </summary>
    public class ModifierConfig
    {
        public float Floor = -3f;
        public float Ceiling = 3f;

        /// <summary>Modifier gained per in-game day with a qualifying action.</summary>
        public float GrowthPerDay = 0.25f;

        /// <summary>Modifier lost per in-game day without a qualifying action (after GraceDays).</summary>
        public float DecayPerDay = 0.25f;

        /// <summary>In-game days after the last qualifying action before decay starts.</summary>
        public float GraceDays = 0f;

        /// <summary>
        /// Clamps this config to sane hard bounds so a bad manual edit can't
        /// produce broken or crash-prone behavior. Called after every load
        /// and every reload, never trust the file blindly.
        /// </summary>
        public void Sanitize(ILogger logger, string label)
        {
            const float hardBound = 10f;

            float origFloor = Floor;
            float origCeiling = Ceiling;

            Floor = GameMath.Clamp(Floor, -hardBound, hardBound);
            Ceiling = GameMath.Clamp(Ceiling, -hardBound, hardBound);

            if (Floor > Ceiling)
            {
                logger?.Warning("[betterhusbandry] {0}.Floor ({1}) was greater than {0}.Ceiling ({2}). Swapping them.",
                    label, Floor, Ceiling);
                float tmp = Floor;
                Floor = Ceiling;
                Ceiling = tmp;
            }

            if (GrowthPerDay < 0)
            {
                logger?.Warning("[betterhusbandry] {0}.GrowthPerDay was negative, clamping to 0.", label);
                GrowthPerDay = 0;
            }

            if (DecayPerDay < 0)
            {
                logger?.Warning("[betterhusbandry] {0}.DecayPerDay was negative, clamping to 0.", label);
                DecayPerDay = 0;
            }

            if (GraceDays < 0)
            {
                GraceDays = 0;
            }

            if (origFloor != Floor || origCeiling != Ceiling)
            {
                logger?.Warning("[betterhusbandry] {0} bounds clamped to hard limits: Floor={1}, Ceiling={2}.",
                    label, Floor, Ceiling);
            }
        }
    }

    public class FeedModifierConfig : ModifierConfig
    {
        /// <summary>
        /// If true, the feed modifier will only grow if the animal is at or
        /// above a certain weight threshold. If false, the feed modifier will
        /// grow regardless of weight.
        /// </summary>
        public float GoodWeightThreshold = 0.95f;

        public float OkWeightThreshold = 0.75f;

        public new void Sanitize(ILogger logger, string label)
        {
            base.Sanitize(logger, label);

            if (GoodWeightThreshold < 0 || GoodWeightThreshold > 1)
            {
                logger?.Warning("[betterhusbandry] {0}.GoodWeightThreshold was out of range [0,1], clamping.", label);
                GoodWeightThreshold = GameMath.Clamp(GoodWeightThreshold, 0f, 1f);
            }

            if (OkWeightThreshold < 0 || OkWeightThreshold > 1)
            {
                logger?.Warning("[betterhusbandry] {0}.OkWeightThreshold was out of range [0,1], clamping.", label);
                OkWeightThreshold = GameMath.Clamp(OkWeightThreshold, 0f, 1f);
            }

            if (OkWeightThreshold > GoodWeightThreshold)
            {
                logger?.Warning("[betterhusbandry] {0}.OkWeightThreshold ({1}) was greater than GoodWeightThreshold ({2}), swapping.",
                    label, OkWeightThreshold, GoodWeightThreshold);
                float tmp = OkWeightThreshold;
                OkWeightThreshold = GoodWeightThreshold;
                GoodWeightThreshold = tmp;
            }
        }
    }

    /// <summary>
    /// Root config. Deliberately does NOT expose the effective-generation
    /// floor/ceiling (0-10) - that range is hardcoded in
    /// EntityBehaviorbetterhusbandry.RecomputeEffectiveGeneration because
    /// vanilla's milking-rejection and aggression/flee tables are built
    /// around it, and a bad edit there risks an out-of-range read in code
    /// this mod doesn't control.
    /// </summary>
    public class betterhusbandryConfig
    {
        public FeedModifierConfig FeedMod = new FeedModifierConfig();
        public ModifierConfig InteractMod = new ModifierConfig();

        public void Sanitize(ILogger logger)
        {
            FeedMod?.Sanitize(logger, nameof(FeedMod));
            InteractMod?.Sanitize(logger, nameof(InteractMod));
        }
    }
}
